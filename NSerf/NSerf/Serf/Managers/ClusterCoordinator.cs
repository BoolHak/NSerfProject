// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Serf.Helpers;

namespace NSerf.Serf.Managers;

/// <summary>
/// Manages Serf instance state transitions and cluster coordination operations.
/// Handles: Join, Leave, and Shutdown state management.
/// Thread-safe via internal locking.
/// 
/// State Transitions (from Go serf.go):
/// - Alive → Leaving → Left
/// - Any state → Shutdown (forceful)
/// - Shutdown is terminal (no transitions out)
/// 
/// Reference: Go serf.go State enum and Leave/Shutdown methods
/// </summary>
public class ClusterCoordinator : IDisposable
{
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private SerfState _currentState;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new ClusterCoordinator in Alive state.
    /// </summary>
    public ClusterCoordinator(ILogger? logger)
    {
        _logger = logger;
        _currentState = SerfState.SerfAlive;
    }

    /// <summary>
    /// Gets the current state of the Serf instance.
    /// Thread-safe read operation.
    /// Returns Shutdown if already disposed.
    /// </summary>
    public SerfState GetCurrentState()
    {
        if (_disposed)
            return SerfState.SerfShutdown;
        
        try
        {
            return LockHelper.WithLock(_stateLock, () => _currentState);
        }
        catch (ObjectDisposedException)
        {
            return SerfState.SerfShutdown;
        }
    }

    /// <summary>
    /// Attempts to transition from Alive to Leaving state.
    /// This is the first step in graceful shutdown.
    /// Reference: Go serf.go Leave() method
    /// </summary>
    /// <returns>True if transition succeeded, false if already in non-Alive state or disposed</returns>
    public bool TryTransitionToLeaving()
    {
        if (_disposed)
            return false;
        
        try
        {
            return LockHelper.WithLock(_stateLock, () =>
        {
            if (_currentState != SerfState.SerfAlive)
            {
                _logger?.LogWarning("[ClusterCoordinator] Cannot transition to Leaving from {CurrentState}", _currentState);
                return false;
            }

            _currentState = SerfState.SerfLeaving;
            _logger?.LogInformation("[ClusterCoordinator] Transitioned to Leaving");
            return true;
            });
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to transition from Leaving to Left state.
    /// This completes the graceful leave process.
    /// Reference: Go serf.go Leave() method after broadcast timeout
    /// </summary>
    /// <returns>True if transition succeeded, false if not in Leaving state or disposed</returns>
    public bool TryTransitionToLeft()
    {
        if (_disposed)
            return false;
        
        try
        {
            return LockHelper.WithLock(_stateLock, () =>
        {
            if (_currentState != SerfState.SerfLeaving)
            {
                _logger?.LogWarning("[ClusterCoordinator] Cannot transition to Left from {CurrentState}", _currentState);
                return false;
            }

            _currentState = SerfState.SerfLeft;
            _logger?.LogInformation("[ClusterCoordinator] Transitioned to Left");
            return true;
            });
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Transitions to Shutdown state from any state.
    /// Shutdown is terminal - no transitions allowed after this.
    /// Reference: Go serf.go Shutdown() method
    /// </summary>
    /// <returns>True if transition succeeded, false if already shutdown or disposed</returns>
    public bool TryTransitionToShutdown()
    {
        if (_disposed)
            return false;
        
        try
        {
            return LockHelper.WithLock(_stateLock, () =>
        {
            if (_currentState == SerfState.SerfShutdown)
            {
                _logger?.LogDebug("[ClusterCoordinator] Already in Shutdown state");
                return false;
            }

            var previousState = _currentState;
            _currentState = SerfState.SerfShutdown;
            _logger?.LogInformation("[ClusterCoordinator] Transitioned to Shutdown from {PreviousState}", previousState);
            return true;
            });
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the instance is shutdown.
    /// Thread-safe operation.
    /// Returns true if disposed (disposal implies shutdown).
    /// </summary>
    public bool IsShutdown()
    {
        if (_disposed)
            return true;
        
        try
        {
            return LockHelper.WithLock(_stateLock, () => _currentState == SerfState.SerfShutdown);
        }
        catch (ObjectDisposedException)
        {
            return true; // Disposed = Shutdown
        }
    }

    /// <summary>
    /// Checks if the instance is in Leaving state.
    /// Thread-safe operation.
    /// Returns false if disposed.
    /// </summary>
    public bool IsLeaving()
    {
        if (_disposed)
            return false;
        
        try
        {
            return LockHelper.WithLock(_stateLock, () => _currentState == SerfState.SerfLeaving);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Executes an action only if currently in Leaving state.
    /// Used for leave-specific cleanup operations.
    /// Thread-safe operation.
    /// Does nothing if disposed.
    /// </summary>
    public void ExecuteInLeavingState(Action action)
    {
        if (_disposed)
            return;
        
        try
        {
            LockHelper.WithLock(_stateLock, () =>
            {
                if (_currentState == SerfState.SerfLeaving)
                {
                    action();
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }
    }

    /// <summary>
    /// Executes an action if not shutdown, returns false if shutdown.
    /// Used to guard operations that should not run after shutdown.
    /// Thread-safe operation.
    /// Returns false if disposed.
    /// </summary>
    public bool ExecuteIfNotShutdown(Action action)
    {
        if (_disposed)
            return false;
        
        try
        {
            return LockHelper.WithLock(_stateLock, () =>
            {
                if (_currentState == SerfState.SerfShutdown)
                {
                    return false;
                }

                action();
                return true;
            });
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets a snapshot of the current state information.
    /// Thread-safe operation.
    /// Returns Shutdown snapshot if disposed.
    /// </summary>
    public StateSnapshot GetStateSnapshot()
    {
        if (_disposed)
        {
            return new StateSnapshot
            {
                CurrentState = SerfState.SerfShutdown,
                IsShutdown = true,
                IsLeaving = false
            };
        }
        
        try
        {
            return LockHelper.WithLock(_stateLock, () => new StateSnapshot
            {
                CurrentState = _currentState,
                IsShutdown = _currentState == SerfState.SerfShutdown,
                IsLeaving = _currentState == SerfState.SerfLeaving
            });
        }
        catch (ObjectDisposedException)
        {
            return new StateSnapshot
            {
                CurrentState = SerfState.SerfShutdown,
                IsShutdown = true,
                IsLeaving = false
            };
        }
    }

    /// <summary>
    /// Disposes the coordinator and transitions to shutdown if not already.
    /// Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        
        // Mark as disposed BEFORE disposing lock to prevent new operations
        _disposed = true;
        
        // Transition to shutdown first (needs lock)
        try
        {
            // Directly set state without lock since we're disposing
            _currentState = SerfState.SerfShutdown;
            _logger?.LogInformation("[ClusterCoordinator] Disposed - set to Shutdown state");
        }
        finally
        {
            // Then dispose the lock
            _stateLock?.Dispose();
        }
    }
}

/// <summary>
/// Snapshot of coordinator state at a point in time.
/// </summary>
public class StateSnapshot
{
    public SerfState CurrentState { get; init; }
    public bool IsShutdown { get; init; }
    public bool IsLeaving { get; init; }
}
