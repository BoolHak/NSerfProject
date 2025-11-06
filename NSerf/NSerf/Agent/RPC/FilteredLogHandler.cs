// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Text.RegularExpressions;
using static NSerf.Agent.CircularLogWriter;

namespace NSerf.Agent.RPC;

/// <summary>
/// Applies log-level filtering before delegating to an inner ILogHandler.
/// Mirrors Go's monitor-level filtering semantics.
/// </summary>
public sealed class FilteredLogHandler(ILogHandler inner, LogLevel minLevel) : ILogHandler
{
    private readonly ILogHandler _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    private static readonly Regex LevelRegex = new(
        @"^\[(TRACE|DEBUG|INFO|WARN|ERR)\]",
        RegexOptions.Compiled);

    public void HandleLog(string log)
    {
        if (ShouldWrite(log))
        {
            _inner.HandleLog(log);
        }
    }

    private bool ShouldWrite(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;

        var match = LevelRegex.Match(line);
        if (!match.Success)
        {
            // No level prefix, write it
            return true;
        }

        var levelStr = match.Groups[1].Value;
        var level = levelStr switch
        {
            "TRACE" => LogLevel.Trace,
            "DEBUG" => LogLevel.Debug,
            "INFO"  => LogLevel.Info,
            "WARN"  => LogLevel.Warn,
            "ERR"   => LogLevel.Error,
            _        => LogLevel.Info
        };

        return level.IsAtLeast(minLevel);
    }
}
