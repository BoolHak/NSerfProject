// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Serf;
using NSerf.Serf.Events;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

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
    private static readonly Regex SanitizeTagRegex = new(
        @"[^A-Z0-9_]",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

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
        IEvent? evt = null)
    {
        var output = new CircularBuffer(MaxBufferSize);
        var actualTimeout = timeout ?? DefaultTimeout;
        var result = new ScriptResult();

        using var process = CreateProcess(script, envVars);
        using var slowWarningTimer = CreateSlowScriptTimer(script, logger, result.Warnings);

        AttachOutputHandlers(process, output);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WriteStdinAsync(process, stdin);
        await WaitForCompletionAsync(process, script, actualTimeout);

        result = BuildResult(process, output, script, logger, result);
        await HandleQueryResponseAsync(evt, result, logger);

        return result;
    }

    private static Process CreateProcess(string script, Dictionary<string, string> envVars)
    {
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

        ConfigureEnvironmentVariables(psi, envVars);

        return new Process { StartInfo = psi };
    }

    private static void ConfigureEnvironmentVariables(
        ProcessStartInfo psi,
        Dictionary<string, string> envVars)
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                psi.Environment[key] = value;
            }
        }

        foreach (var (key, value) in envVars)
        {
            psi.Environment[key] = value;
        }
    }

    private static Timer CreateSlowScriptTimer(string script, ILogger? logger, List<string> warnings)
    {
        var warningShown = false;

        return new Timer(_ =>
        {
            if (warningShown) return;

            warningShown = true;
            var warning = $"Script '{script}' slow, execution exceeding {SlowScriptWarnTime.TotalSeconds}s";
            warnings.Add(warning);
            logger?.LogWarning("[Agent/Script] {Warning}", warning);
        }, null, SlowScriptWarnTime, Timeout.InfiniteTimeSpan);
    }

    private static void AttachOutputHandlers(Process process, CircularBuffer output)
    {
        void WriteOutput(object _, DataReceivedEventArgs e)
        {
            if (e.Data is not null)
            {
                output.Write(Encoding.UTF8.GetBytes(e.Data + "\n"));
            }
        }

        process.OutputDataReceived += WriteOutput;
        process.ErrorDataReceived += WriteOutput;
    }

    private static async Task WriteStdinAsync(Process process, string? stdin)
    {
        if (string.IsNullOrEmpty(stdin))
        {
            process.StandardInput.Close();
            return;
        }

        try
        {
            await process.StandardInput.WriteAsync(stdin);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }
        catch
        {
            // Script may not read stdin
        }

    }

    private static async Task WaitForCompletionAsync(
        Process process,
        string script,
        TimeSpan timeout)
    {
        var completed = await Task.Run(() =>
            process.WaitForExit((int)timeout.TotalMilliseconds));

        if (completed)
        {
            await process.WaitForExitAsync();
            return;
        }

        KillProcess(process);
        throw new TimeoutException(
            $"Script '{script}' exceeded timeout of {timeout.TotalSeconds}s");
    }

    private static void KillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may have already exited
        }
    }

    private static ScriptResult BuildResult(
        Process process,
        CircularBuffer output,
        string script,
        ILogger? logger,
        ScriptResult result)
    {
        result.ExitCode = process.ExitCode;
        result.TotalWritten = output.TotalWritten;
        result.WasTruncated = output.WasTruncated;
        result.Output = output.GetString().TrimEnd();

        if (result.WasTruncated)
        {
            AddTruncationWarning(result, script, logger);
        }

        logger?.LogDebug("[Agent/Script] Event script output: {Output}", result.Output);

        return result;
    }

    private static void AddTruncationWarning(
        ScriptResult result,
        string script,
        ILogger? logger)
    {
        var warning = $"Script '{script}' generated {result.TotalWritten} bytes of output, " +
                      $"truncated to {MaxBufferSize}";
        result.Warnings.Add(warning);
        logger?.LogWarning("[Agent/Script] {Warning}", warning);
    }

    private static async Task HandleQueryResponseAsync(
        IEvent? evt,
        ScriptResult result,
        ILogger? logger)
    {
        if (evt is not Query query || result.TotalWritten == 0)
        {
            return;
        }

        try
        {
            await query.RespondAsync(Encoding.UTF8.GetBytes(result.Output));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "[Agent/Script] Failed to respond to query '{QueryName}'",
                query.Name);
        }
    }




    public static Dictionary<string, string> BuildEnvironmentVariables(Member self, IEvent evt)
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

    public static string? BuildStdin(IEvent evt)
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

    private static string GetEventTypeName(IEvent evt)
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
