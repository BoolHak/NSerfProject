// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Agent;

/// <summary>
/// Circular buffer log writer for the monitor command.
/// Maps to: Go's log_writer.go
/// </summary>
public class CircularLogWriter(int bufferSize = 512) : IDisposable
{
    private readonly string[] _logs = new string[bufferSize];
    private int _index;
    private readonly List<ILogHandler> _handlers = [];
    private readonly object _lock = new();
    private bool _disposed = false;

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
            handlers = [.. _handlers];
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
        var logsToSend = !string.IsNullOrEmpty(_logs[_index])
        ? _logs.Skip(_index).Concat(_logs.Take(_index))
        : _logs.Take(_index);

        foreach (var log in logsToSend.Where(log => !string.IsNullOrEmpty(log)))
        {
            handler.HandleLog(log);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose managed resources
                _handlers.Clear();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Handler interface for receiving log lines.
    /// </summary>
    public interface ILogHandler
    {
        void HandleLog(string log);
    }
}

