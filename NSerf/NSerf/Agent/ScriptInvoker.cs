// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NSerf.Serf;
using NSerf.Serf.Events;

namespace NSerf.Agent;

/// <summary>
/// Executes event handler scripts with proper environment and I/O handling.
/// Maps to: Go's invoke.go in serf/cmd/serf/command/agent/
/// </summary>
public class ScriptInvoker
{
    private const int MaxBufferSize = 8 * 1024;  // 8KB output limit
    private static readonly TimeSpan SlowScriptWarnTime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly Regex SanitizeTagRegex = new(@"[^A-Z0-9_]", RegexOptions.Compiled);

    public class ScriptResult
    {
        public string Output { get; set; } = string.Empty;
        public long TotalWritten { get; set; }
        public bool WasTruncated { get; set; }
        public int ExitCode { get; set; }
        public List<string> Warnings { get; } = new();
    }

    public static async Task<ScriptResult> ExecuteAsync(
        string script,
        Dictionary<string, string> envVars,
        string? stdin,
        ILogger? logger = null,
        TimeSpan? timeout = null,
        Event? evt = null)
    {
        var result = new ScriptResult();
        var output = new CircularBuffer(MaxBufferSize);

        var (shell, flag) = GetShellCommand();

        var psi = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"{flag} {script}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Inherit OS environment + add Serf-specific vars
        foreach (System.Collections.DictionaryEntry kvp in Environment.GetEnvironmentVariables())
        {
            if (kvp.Key != null && kvp.Value != null)
                psi.Environment[(string)kvp.Key] = (string)kvp.Value;
        }

        foreach (var kvp in envVars)
        {
            psi.Environment[kvp.Key] = kvp.Value;
        }

        using var process = new Process { StartInfo = psi };

        // Slow script warning timer
        var slowWarningShown = false;
        var slowTimer = new System.Threading.Timer(_ =>
        {
            if (!slowWarningShown)
            {
                slowWarningShown = true;
                var warning = $"Script '{script}' slow, execution exceeding {SlowScriptWarnTime.TotalSeconds}s";
                lock (result.Warnings)
                {
                    result.Warnings.Add(warning);
                }
                logger?.LogWarning("[Agent/Script] {Warning}", warning);
            }
        }, null, SlowScriptWarnTime, Timeout.InfiniteTimeSpan);

        // Capture stdout and stderr to same buffer
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                output.Write(Encoding.UTF8.GetBytes(e.Data + "\n"));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                output.Write(Encoding.UTF8.GetBytes(e.Data + "\n"));
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Write stdin in background (prevents deadlock)
        if (!string.IsNullOrEmpty(stdin))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await process.StandardInput.WriteAsync(stdin);
                    await process.StandardInput.FlushAsync();
                    process.StandardInput.Close();
                }
                catch
                {
                    // Ignore - script may not read stdin
                }
            });
        }
        else
        {
            process.StandardInput.Close();
        }

        // Wait for completion with timeout
        var actualTimeout = timeout ?? DefaultTimeout;
        var completed = await Task.Run(() => process.WaitForExit((int)actualTimeout.TotalMilliseconds));

        // Stop the timer (matches Go's slowTimer.Stop() - no delay needed)
        slowTimer.Dispose();

        if (!completed)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // Process may have already exited
            }
            throw new TimeoutException($"Script '{script}' exceeded timeout of {actualTimeout.TotalSeconds}s");
        }

        // Wait for async output readers to finish
        await process.WaitForExitAsync();

        result.ExitCode = process.ExitCode;
        result.TotalWritten = output.TotalWritten;
        result.WasTruncated = output.WasTruncated;
        result.Output = output.GetString().TrimEnd();

        // Warn if output was truncated
        if (result.WasTruncated)
        {
            var warning = $"Script '{script}' generated {result.TotalWritten} bytes of output, truncated to {MaxBufferSize}";
            result.Warnings.Add(warning);
            logger?.LogWarning("[Agent/Script] {Warning}", warning);
        }

        // Debug log output
        logger?.LogDebug("[Agent/Script] Event script output: {Output}", result.Output);

        // Auto-respond to query if output present
        if (evt is Query query && result.TotalWritten > 0)
        {
            try
            {
                await query.RespondAsync(Encoding.UTF8.GetBytes(result.Output));
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[Agent/Script] Failed to respond to query '{QueryName}'", query.Name);
            }
        }

        return result;
    }

    public static Dictionary<string, string> BuildEnvironmentVariables(Member self, Event evt)
    {
        var envVars = new Dictionary<string, string>
        {
            ["SERF_EVENT"] = GetEventTypeName(evt),
            ["SERF_SELF_NAME"] = self.Name,
            ["SERF_SELF_ROLE"] = self.Tags.GetValueOrDefault("role", "")
        };

        // Add all tags as SERF_TAG_* (sanitized)
        foreach (var tag in self.Tags)
        {
            var sanitizedName = SanitizeTagName(tag.Key);
            envVars[$"SERF_TAG_{sanitizedName}"] = tag.Value;
        }

        // User event specific
        if (evt is UserEvent userEvt)
        {
            envVars["SERF_USER_EVENT"] = userEvt.Name;
            envVars["SERF_USER_LTIME"] = userEvt.LTime.ToString();
        }

        // Query specific
        if (evt is Query query)
        {
            envVars["SERF_QUERY_NAME"] = query.Name;
            envVars["SERF_QUERY_LTIME"] = query.LTime.ToString();
        }

        return envVars;
    }

    public static string? BuildStdin(Event evt)
    {
        if (evt is MemberEvent memberEvt)
            return BuildMemberEventStdin(memberEvt);

        if (evt is UserEvent userEvt)
            return PreparePayload(userEvt.Payload);

        if (evt is Query query)
            return PreparePayload(query.Payload);

        return null;
    }

    public static string BuildMemberEventStdin(MemberEvent evt)
    {
        var sb = new StringBuilder();

        foreach (var member in evt.Members)
        {
            var role = member.Tags.GetValueOrDefault("role", "");
            var tags = string.Join(",", member.Tags.Select(kvp => $"{kvp.Key}={kvp.Value}"));

            sb.AppendFormat("{0}\t{1}\t{2}\t{3}\n",
                EventClean(member.Name),
                member.Addr,
                EventClean(role),
                EventClean(tags));
        }

        return sb.ToString();
    }

    public static string PreparePayload(byte[]? payload)
    {
        if (payload == null || payload.Length == 0)
            return string.Empty;

        var str = Encoding.UTF8.GetString(payload);

        // Append newline if missing (scripts expect newline-terminated input)
        if (!str.EndsWith('\n'))
            str += '\n';

        return str;
    }

    public static string EventClean(string value)
    {
        // Escape tabs and newlines to prevent breaking tab-separated format
        return value
            .Replace("\t", "\\t")
            .Replace("\n", "\\n");
    }

    public static string SanitizeTagName(string name)
    {
        // Convert to uppercase and replace non-alphanumeric with underscore
        return SanitizeTagRegex.Replace(name.ToUpper(), "_");
    }

    private static (string shell, string flag) GetShellCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ("cmd", "/C");
        else
            return ("/bin/sh", "-c");
    }

    private static string GetEventTypeName(Event evt)
    {
        return evt switch
        {
            MemberEvent me when me.Type == EventType.MemberJoin => "member-join",
            MemberEvent me when me.Type == EventType.MemberLeave => "member-leave",
            MemberEvent me when me.Type == EventType.MemberFailed => "member-failed",
            MemberEvent me when me.Type == EventType.MemberUpdate => "member-update",
            MemberEvent me when me.Type == EventType.MemberReap => "member-reap",
            UserEvent => "user",
            Query => "query",
            _ => "unknown"
        };
    }
}
