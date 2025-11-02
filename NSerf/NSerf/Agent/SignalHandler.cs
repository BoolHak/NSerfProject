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
    private readonly List<SignalCallback> _callbacks = [];
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
            if (_disposed)
                return;
            _callbacks.Add(callback);
        }
    }

    public void TriggerSignal(Signal signal)
    {
        SignalCallback[] callbacks;
        lock (_lock)
        {
            if (_disposed)
                return;
            callbacks = [.. _callbacks];
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

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            // Free managed resources
            Console.CancelKeyPress -= OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        }
        
        _disposed = true;
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
