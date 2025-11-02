// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Text;

namespace NSerf.Agent;

/// <summary>
/// A writer that can be flushed to an underlying writer.
/// Buffers writes until Flush() is called.
/// Maps to: Go's gatedwriter pattern
/// </summary>
public class GatedWriter(TextWriter writer) : TextWriter
{
    private readonly TextWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly StringBuilder _buffer = new();
    private readonly object _lock = new();
    private bool _flushed;

    public override Encoding Encoding => _writer.Encoding;

    public override void Write(char value)
    {
        lock (_lock)
        {
            if (_flushed)
            {
                _writer.Write(value);
            }
            else
            {
                _buffer.Append(value);
            }
        }
    }

    public override void Write(string? value)
    {
        if (value == null) return;

        lock (_lock)
        {
            if (_flushed)
            {
                _writer.Write(value);
            }
            else
            {
                _buffer.Append(value);
            }
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_lock)
        {
            if (_flushed)
            {
                _writer.WriteLine(value);
            }
            else
            {
                _buffer.AppendLine(value);
            }
        }
    }

    public override void Flush()
    {
        lock (_lock)
        {
            if (!_flushed)
            {
                _flushed = true;
                if (_buffer.Length > 0)
                {
                    _writer.Write(_buffer.ToString());
                    _writer.Flush();
                }
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _buffer.Clear();
            _flushed = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Flush();
        }
        base.Dispose(disposing);
    }
}
