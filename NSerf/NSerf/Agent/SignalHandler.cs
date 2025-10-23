// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;

namespace NSerf.Agent;

public enum Signal
{
    SIGINT,
    SIGTERM,
    SIGHUP
}

public delegate void SignalCallback(Signal signal);

/// <summary>
/// Cross-platform signal handling.
/// Windows: Console.CancelKeyPress for SIGINT, custom events for others
/// Unix: POSIX signals
/// </summary>
public class SignalHandler : IDisposable
{
    private readonly List<SignalCallback> _callbacks = new();
    private readonly object _lock = new();
    private bool _disposed;

    public SignalHandler()
    {
        // Register for Ctrl+C (SIGINT on Unix, Ctrl+C on Windows)
        Console.CancelKeyPress += OnCancelKeyPress;
        
        // Register for process exit (SIGTERM equivalent)
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public void RegisterCallback(SignalCallback callback)
    {
        lock (_lock)
        {
            _callbacks.Add(callback);
        }
    }

    public void TriggerSignal(Signal signal)
    {
        SignalCallback[] callbacks;
        lock (_lock)
        {
            callbacks = _callbacks.ToArray();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                callback(signal);
            }
            catch
            {
                // Ignore callback errors
            }
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Prevent immediate termination
        TriggerSignal(Signal.SIGINT);
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        TriggerSignal(Signal.SIGTERM);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Console.CancelKeyPress -= OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }
}
