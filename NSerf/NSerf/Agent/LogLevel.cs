// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Agent;

/// <summary>
/// Log levels for filtering log output.
/// Maps to: Go's logutils levels
/// </summary>
public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error
}

public static class LogLevelExtensions
{
    public static string ToPrefix(this LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => "[TRACE]",
            LogLevel.Debug => "[DEBUG]",
            LogLevel.Info => "[INFO]",
            LogLevel.Warn => "[WARN]",
            LogLevel.Error => "[ERR]",
            _ => "[INFO]"
        };
    }

    public static LogLevel FromString(string level)
    {
        return level.ToLower() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info" => LogLevel.Info,
            "warn" or "warning" => LogLevel.Warn,
            "error" or "err" => LogLevel.Error,
            _ => LogLevel.Info
        };
    }

    public static bool IsAtLeast(this LogLevel current, LogLevel minimum)
    {
        return current >= minimum;
    }
}
