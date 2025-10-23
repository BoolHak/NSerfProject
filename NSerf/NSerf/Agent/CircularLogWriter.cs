// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Agent;

/// <summary>
/// Circular buffer log writer for the monitor command.
/// Maps to: Go's log_writer.go
/// </summary>
public class CircularLogWriter : IDisposable
{
    private readonly string[] _logs;
    private int _index;
    private readonly List<ILogHandler> _handlers = new();
    private readonly object _lock = new();
    private bool _disposed;

    public CircularLogWriter(int bufferSize = 512)
    {
        _logs = new string[bufferSize];
    }

    /// <summary>
    /// Register a handler and send it the backlog.
    /// </summary>
    public void RegisterHandler(ILogHandler handler)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            // Send backlog first
            SendBacklog(handler);

            // Then add to handlers for real-time logs
            _handlers.Add(handler);
        }
    }

    /// <summary>
    /// Deregister a handler.
    /// </summary>
    public void DeregisterHandler(ILogHandler handler)
    {
        lock (_lock)
        {
            _handlers.Remove(handler);
        }
    }

    /// <summary>
    /// Write a log line to the buffer and all handlers.
    /// </summary>
    public void WriteLine(string log)
    {
        if (string.IsNullOrEmpty(log))
            return;

        // Strip trailing newline
        if (log.EndsWith('\n'))
            log = log.TrimEnd('\n');

        ILogHandler[] handlers;
        lock (_lock)
        {
            if (_disposed)
                return;

            // Store in circular buffer
            _logs[_index] = log;
            _index = (_index + 1) % _logs.Length;

            // Copy handlers for lock-free iteration
            handlers = _handlers.ToArray();
        }

        // Notify all handlers
        foreach (var handler in handlers)
        {
            try
            {
                handler.HandleLog(log);
            }
            catch
            {
                // Ignore handler errors
            }
        }
    }

    private void SendBacklog(ILogHandler handler)
    {
        // If buffer has wrapped (oldest log at current index)
        if (!string.IsNullOrEmpty(_logs[_index]))
        {
            // Send from index to end
            for (int i = _index; i < _logs.Length; i++)
            {
                if (!string.IsNullOrEmpty(_logs[i]))
                    handler.HandleLog(_logs[i]);
            }
        }

        // Send from start to index
        for (int i = 0; i < _index; i++)
        {
            if (!string.IsNullOrEmpty(_logs[i]))
                handler.HandleLog(_logs[i]);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _handlers.Clear();
        }
    }
}

/// <summary>
/// Handler interface for receiving log lines.
/// </summary>
public interface ILogHandler
{
    void HandleLog(string log);
}
