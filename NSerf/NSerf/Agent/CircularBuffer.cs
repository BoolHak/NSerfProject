// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Agent;

/// <summary>
/// Circular buffer with fixed size that tracks total written bytes.
/// Oldest data is overwritten when buffer is full.
/// Maps to: Go's circbuf.Buffer from armon/circbuf
/// </summary>
public class CircularBuffer
{
    private readonly byte[] _buffer;
    private int _writePos;
    private long _totalWritten;
    private bool _isFull;

    public int Size => _buffer.Length;
    public long TotalWritten => _totalWritten;
    public bool WasTruncated => _totalWritten > _buffer.Length;

    public CircularBuffer(int size)
    {
        if (size <= 0)
            throw new ArgumentException("Buffer size must be positive", nameof(size));
        
        _buffer = new byte[size];
    }

    public void Write(byte[] data)
    {
        if (data == null || data.Length == 0)
            return;

        _totalWritten += data.Length;

        foreach (var b in data)
        {
            _buffer[_writePos] = b;
            _writePos = (_writePos + 1) % _buffer.Length;
            
            if (_writePos == 0)
                _isFull = true;
        }
    }

    public byte[] GetBytes()
    {
        if (!_isFull)
        {
            // Haven't wrapped yet, return from start to write position
            var result = new byte[_writePos];
            Array.Copy(_buffer, 0, result, 0, _writePos);
            return result;
        }
        else
        {
            // Wrapped, need to reconstruct in correct order
            var result = new byte[_buffer.Length];
            var firstChunkLen = _buffer.Length - _writePos;
            Array.Copy(_buffer, _writePos, result, 0, firstChunkLen);
            Array.Copy(_buffer, 0, result, firstChunkLen, _writePos);
            return result;
        }
    }

    public string GetString() => System.Text.Encoding.UTF8.GetString(GetBytes());

    public void Reset()
    {
        _writePos = 0;
        _totalWritten = 0;
        _isFull = false;
        Array.Clear(_buffer, 0, _buffer.Length);
    }
}
