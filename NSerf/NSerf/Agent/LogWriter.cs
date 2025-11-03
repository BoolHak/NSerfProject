// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using System.Text.RegularExpressions;

namespace NSerf.Agent;

/// <summary>
/// Filters log output based on log level.
/// Maps to: Go's logutils.LevelFilter
/// </summary>
public class LogWriter(TextWriter writer, LogLevel minLevel) : TextWriter
{
    private readonly TextWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly LogLevel _minLevel = minLevel;
    private readonly object _lock = new();
    private static readonly Regex LevelRegex = new(
        @"^\[(TRACE|DEBUG|INFO|WARN|ERR)\]",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public override Encoding Encoding => GetWriterEncoding();

    private Encoding GetWriterEncoding()
    {
        lock (_lock)
        {
            return _writer.Encoding;
        }
    }
    public override void Write(char value)
    {
        lock (_lock)
        {
            _writer.Write(value);
        }
    }

    public override void Write(string? value)
    {
        if (value == null) return;

        lock (_lock)
        {
            if (ShouldWrite(value))
            {
                _writer.Write(value);
            }
        }
    }

    public override void WriteLine(string? value)
    {
        if (value == null)
        {
            lock (_lock)
            {
                _writer.WriteLine();
            }
            return;
        }

        lock (_lock)
        {
            if (ShouldWrite(value))
            {
                _writer.WriteLine(value);
            }
        }
    }

    private bool ShouldWrite(string line)
    {
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
            "INFO" => LogLevel.Info,
            "WARN" => LogLevel.Warn,
            "ERR" => LogLevel.Error,
            _ => LogLevel.Info
        };

        return level.IsAtLeast(_minLevel);
    }

    public override void Flush()
    {
        lock (_lock)
        {
            _writer.Flush();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                _writer.Flush();
            }
        }
        base.Dispose(disposing);
    }
}
