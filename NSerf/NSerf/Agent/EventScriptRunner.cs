// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using NSerf.Serf;

namespace NSerf.Agent;

public class EventScriptRunner
{
    private readonly Dictionary<string, string> _scripts;

    public EventScriptRunner(string[] eventHandlers)
    {
        _scripts = [];
        foreach (var handler in eventHandlers)
        {
            ParseEventHandler(handler);
        }
    }

    private void ParseEventHandler(string handler)
    {
        // Format: "event-type=script.sh" or just "script.sh" for all events
        var parts = handler.Split('=', 2);
        if (parts.Length == 2)
        {
            _scripts[parts[0]] = parts[1];
        }
        else
        {
            _scripts["*"] = parts[0];  // All events
        }
    }

    public async Task<int> HandleEventAsync(string eventName, byte[] payload, ulong ltime, CancellationToken cancellationToken = default)
    {
        if (!_scripts.TryGetValue(eventName, out var script) && !_scripts.TryGetValue("*", out script))
            return 0;

        var env = BuildEnvironment(eventName, ltime);
        return await ExecuteScriptAsync(script, env, payload, cancellationToken);
    }

    public async Task<int> HandleMemberEventAsync(string eventType, Member member, CancellationToken cancellationToken = default)
    {
        if (!_scripts.TryGetValue(eventType, out var script) && !_scripts.TryGetValue("*", out script))
            return 0;

        var env = BuildMemberEnvironment(eventType, member);
        return await ExecuteScriptAsync(script, env, null, cancellationToken);
    }

    private static Dictionary<string, string> BuildEnvironment(string eventName, ulong ltime)
    {
        return new Dictionary<string, string>
        {
            ["SERF_EVENT"] = "user",
            ["SERF_USER_EVENT"] = eventName,
            ["SERF_USER_LTIME"] = ltime.ToString()
        };
    }

    private static Dictionary<string, string> BuildMemberEnvironment(string eventType, Member member)
    {
        return new Dictionary<string, string>
        {
            ["SERF_EVENT"] = eventType,
            ["SERF_SELF_NAME"] = member.Name,
            ["SERF_SELF_ROLE"] = member.Tags.TryGetValue("role", out var role) ? role : "",
            ["SERF_MEMBER_NAME"] = member.Name,
            ["SERF_MEMBER_ADDR"] = member.Addr.ToString(),
            ["SERF_MEMBER_PORT"] = member.Port.ToString(),
            ["SERF_MEMBER_STATUS"] = member.Status.ToString().ToLowerInvariant()
        };
    }

    private static async Task<int> ExecuteScriptAsync(
        string script,
        Dictionary<string, string> env,
        byte[]? input,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetShell(),
            Arguments = GetShellArgs(script),
            UseShellExecute = false,
            RedirectStandardInput = input != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var kvp in env)
        {
            startInfo.Environment[kvp.Key] = kvp.Value;
        }

        using var process = new Process { StartInfo = startInfo };

        process.Start();

        if (input != null)
        {
            await process.StandardInput.BaseStream.WriteAsync(input, cancellationToken);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static string GetShell()
    {
        return OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
    }

    private static string GetShellArgs(string script)
    {
        return OperatingSystem.IsWindows() ? $"/c {script}" : $"-c {script}";
    }
}
