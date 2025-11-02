// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

namespace NSerf.Memberlist;

/// <summary>
/// Manages graceful shutdown of memberlist components.
/// </summary>
public class ShutdownManager
{
    private readonly ILogger? _logger;
    private readonly List<Action> _shutdownActions = new();
    private readonly object _lock = new();
    private bool _isShutdown;
    
    public ShutdownManager(ILogger? logger = null)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Registers an action to be called during shutdown.
    /// </summary>
    public void RegisterShutdownAction(Action action)
    {
        lock (_lock)
        {
            if (_isShutdown)
            {
                throw new InvalidOperationException("Cannot register shutdown actions after shutdown");
            }
            _shutdownActions.Add(action);
        }
    }
    
    /// <summary>
    /// Performs graceful shutdown of all registered components.
    /// </summary>
    public async Task ShutdownAsync()
    {
        lock (_lock)
        {
            if (_isShutdown)
            {
                return;
            }
            _isShutdown = true;
        }
        
        _logger?.LogInformation("Initiating graceful shutdown");
        
        // Execute shutdown actions in reverse order
        for (int i = _shutdownActions.Count - 1; i >= 0; i--)
        {
            try
            {
                _shutdownActions[i]();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during shutdown action {Index}", i);
            }
        }
        
        await Task.CompletedTask;
        _logger?.LogInformation("Shutdown complete");
    }
    
    /// <summary>
    /// Gets whether shutdown has been initiated.
    /// </summary>
    public bool IsShutdown
    {
        get
        {
            lock (_lock)
            {
                return _isShutdown;
            }
        }
    }
}
