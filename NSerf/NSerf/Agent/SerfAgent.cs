using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSerf.Serf;
using NSerf.Serf.Events;

namespace NSerf.Agent;

/// <summary>
/// SerfAgent wraps a Serf instance and provides event handling, 
/// configuration management, and lifecycle control.
/// Ported from Go: serf/cmd/serf/command/agent/agent.go
/// </summary>
public class SerfAgent : IAsyncDisposable
{
    private readonly global::NSerf.Serf.Serf _serf;
    private readonly AgentConfig _agentConfig;
    private readonly ILogger? _logger;
    
    private readonly List<IEventHandler> _eventHandlers = new();
    private readonly object _handlersLock = new();
    
    private readonly System.Threading.Channels.Channel<Event> _eventChannel;
    private readonly System.Threading.Channels.ChannelWriter<Event> _eventWriter;
    
    private Task? _eventLoopTask;
    private readonly CancellationTokenSource _shutdownCts = new();
    private bool _started;
    private bool _disposed;

    private SerfAgent(
        global::NSerf.Serf.Serf serf,
        AgentConfig agentConfig,
        ILogger? logger,
        System.Threading.Channels.ChannelWriter<Event> eventWriter,
        System.Threading.Channels.Channel<Event> eventChannel)
    {
        _serf = serf;
        _agentConfig = agentConfig;
        _logger = logger;
        _eventWriter = eventWriter;
        _eventChannel = eventChannel;
    }

    /// <summary>
    /// Creates a new SerfAgent with the given configuration.
    /// Ported from Go: Create()
    /// </summary>
    public static async Task<SerfAgent> CreateAsync(
        AgentConfig? agentConfig = null,
        Config? serfConfig = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        agentConfig ??= AgentConfig.Default();
        
        // Create event channel for agent
        var eventChannel = System.Threading.Channels.Channel.CreateUnbounded<Event>();
        
        // Create or use provided Serf config
        serfConfig ??= new Config
        {
            NodeName = agentConfig.NodeName,
            MemberlistConfig = new global::NSerf.Memberlist.Configuration.MemberlistConfig
            {
                Name = agentConfig.NodeName,
                BindAddr = "0.0.0.0",
                BindPort = 7946
            }
        };
        
        // Set EventCh so agent receives events
        serfConfig.EventCh = eventChannel.Writer;

        // Apply agent config settings to Serf config
        if (agentConfig.EnableCompression && serfConfig.MemberlistConfig != null)
        {
            serfConfig.MemberlistConfig.EnableCompression = true;
        }

        // Merge tags from agent config if not using tags file
        if (string.IsNullOrEmpty(agentConfig.TagsFile) && agentConfig.Tags.Count > 0)
        {
            foreach (var (key, value) in agentConfig.Tags)
            {
                serfConfig.Tags[key] = value;
            }
        }

        // Load tags from file if specified
        if (!string.IsNullOrEmpty(agentConfig.TagsFile))
        {
            // Check for conflict
            if (agentConfig.Tags.Count > 0)
            {
                throw new InvalidOperationException("Tags config not allowed while using tag files");
            }
            
            await LoadTagsFromFileAsync(agentConfig.TagsFile, serfConfig, logger, cancellationToken);
        }

        // Load keyring from file if specified
        if (!string.IsNullOrEmpty(agentConfig.KeyringFile))
        {
            await LoadKeyringFromFileAsync(agentConfig.KeyringFile, serfConfig, logger, cancellationToken);
        }

        // Create Serf instance
        var serf = await global::NSerf.Serf.Serf.CreateAsync(serfConfig);

        var agent = new SerfAgent(serf, agentConfig, logger, eventChannel.Writer, eventChannel);
        return agent;
    }

    /// <summary>
    /// Starts the agent and begins processing events.
    /// Ported from Go: Start()
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            throw new InvalidOperationException("Agent already started");
        
        if (_disposed)
            throw new ObjectDisposedException(nameof(SerfAgent));

        _logger?.LogInformation("[Agent] Starting Serf agent");

        _started = true;

        // Start event loop
        _eventLoopTask = Task.Run(() => EventLoopAsync(_shutdownCts.Token), _shutdownCts.Token);
        
        // Give event loop a moment to start
        await Task.Delay(100, cancellationToken);
    }

    /// <summary>
    /// Event loop - reads events from Serf and fans out to registered handlers.
    /// Ported from Go: eventLoop()
    /// </summary>
    private async Task EventLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in _eventChannel.Reader.ReadAllAsync(cancellationToken))
            {
                _logger?.LogInformation("[Agent] Received event: {EventType}", evt.EventType());

                // Get snapshot of handlers (thread-safe)
                IEventHandler[] handlers;
                lock (_handlersLock)
                {
                    handlers = _eventHandlers.ToArray();
                }

                // Fan out to all handlers
                foreach (var handler in handlers)
                {
                    try
                    {
                        await handler.HandleEventAsync(evt, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "[Agent] Event handler threw exception");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
            _logger?.LogInformation("[Agent] Event loop cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Agent] Event loop failed");
        }
    }

    /// <summary>
    /// Register an event handler to receive Serf events.
    /// Ported from Go: RegisterEventHandler()
    /// </summary>
    public void RegisterEventHandler(IEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        
        lock (_handlersLock)
        {
            _eventHandlers.Add(handler);
        }
        
        _logger?.LogDebug("[Agent] Registered event handler: {Type}", handler.GetType().Name);
    }

    /// <summary>
    /// Deregister an event handler.
    /// Ported from Go: DeregisterEventHandler()
    /// </summary>
    public void DeregisterEventHandler(IEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        
        lock (_handlersLock)
        {
            _eventHandlers.Remove(handler);
        }
        
        _logger?.LogDebug("[Agent] Deregistered event handler: {Type}", handler.GetType().Name);
    }

    /// <summary>
    /// Returns the underlying Serf instance.
    /// Ported from Go: Serf()
    /// </summary>
    public global::NSerf.Serf.Serf Serf => _serf;

    /// <summary>
    /// Returns the agent configuration.
    /// </summary>
    public AgentConfig Config => _agentConfig;

    #region Convenience Methods (delegate to Serf with logging)

    /// <summary>
    /// Join one or more nodes in the cluster.
    /// Ported from Go: Join()
    /// </summary>
    public async Task<int> JoinAsync(string[] addrs, bool replay = false)
    {
        _logger?.LogInformation("[Agent] Joining: {Addrs}, Replay: {Replay}",
                                string.Join(",", addrs), replay);
        var ignoreOld = !replay;
        var count = await _serf.JoinAsync(addrs, ignoreOld);
        
        if (count > 0)
        {
            _logger?.LogInformation("[Agent] Joined: {Count} nodes", count);
        }
        else
        {
            _logger?.LogWarning("[Agent] No nodes joined");
        }
        
        return count;
    }

    /// <summary>
    /// Force a failed node to leave the cluster.
    /// Ported from Go: ForceLeave()
    /// </summary>
    public async Task ForceLeaveAsync(string node)
    {
        _logger?.LogInformation("[Agent] Force leaving node: {Node}", node);
        await _serf.RemoveFailedNodeAsync(node);
    }

    /// <summary>
    /// Force a failed node to leave and prune it from the member list.
    /// Ported from Go: ForceLeavePrune()
    /// </summary>
    public async Task ForceLeavePruneAsync(string node)
    {
        _logger?.LogInformation("[Agent] Force leaving node (prune): {Node}", node);
        await _serf.RemoveFailedNodeAsync(node, prune: true);
    }

    /// <summary>
    /// Send a user event to the cluster.
    /// Ported from Go: UserEvent()
    /// </summary>
    public async Task UserEventAsync(string name, byte[] payload, bool coalesce = false)
    {
        _logger?.LogDebug("[Agent] Sending user event: {Name}, Coalesce: {Coalesce}, Payload size: {Size}",
                         name, coalesce, payload?.Length ?? 0);
        await _serf.UserEventAsync(name, payload, coalesce);
    }

    /// <summary>
    /// Send a query to the cluster.
    /// Ported from Go: Query()
    /// </summary>
    public async Task<QueryResponse> QueryAsync(string name, byte[]? payload, QueryParam? parameters = null)
    {
        // Prevent use of internal prefix (except for ping)
        if (name.StartsWith("_serf_"))
        {
            if (name != "_serf_ping" || payload != null)
            {
                throw new ArgumentException("Queries cannot contain the '_serf_' prefix", nameof(name));
            }
        }

        _logger?.LogDebug("[Agent] Sending query: {Name}, Payload size: {Size}",
                         name, payload?.Length ?? 0);
        
        return await _serf.QueryAsync(name, payload ?? Array.Empty<byte>(), parameters);
    }

    /// <summary>
    /// Update agent tags with file persistence.
    /// Ported from Go: SetTags()
    /// </summary>
    public async Task SetTagsAsync(Dictionary<string, string> tags, CancellationToken cancellationToken = default)
    {
        // Save to file if configured
        if (!string.IsNullOrEmpty(_agentConfig.TagsFile))
        {
            await SaveTagsToFileAsync(_agentConfig.TagsFile, tags, cancellationToken);
        }

        // Update Serf tags
        await _serf.SetTagsAsync(tags);
    }

    #endregion

    #region Tags File Persistence

    private static async Task LoadTagsFromFileAsync(
        string tagsFile,
        Config serfConfig,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(tagsFile))
        {
            logger?.LogDebug("[Agent] Tags file does not exist: {File}", tagsFile);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(tagsFile, cancellationToken);
            var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            
            if (tags != null)
            {
                foreach (var (key, value) in tags)
                {
                    serfConfig.Tags[key] = value;
                }
                logger?.LogInformation("[Agent] Restored {Count} tag(s) from {File}",
                                      tags.Count, tagsFile);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[Agent] Failed to load tags from {File}", tagsFile);
            throw;
        }
    }

    private async Task SaveTagsToFileAsync(
        string tagsFile,
        Dictionary<string, string> tags,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(tags, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(tagsFile, json, cancellationToken);
            _logger?.LogInformation("[Agent] Saved {Count} tag(s) to {File}", tags.Count, tagsFile);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Agent] Failed to save tags to {File}", tagsFile);
            throw;
        }
    }

    #endregion

    #region Keyring File Loading

    private static async Task LoadKeyringFromFileAsync(
        string keyringFile,
        Config serfConfig,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(keyringFile))
        {
            logger?.LogDebug("[Agent] Keyring file does not exist: {File}", keyringFile);
            return;
        }

        try
        {
            // Keyring file format is JSON array of base64-encoded keys
            var json = await File.ReadAllTextAsync(keyringFile, cancellationToken);
            var keys = JsonSerializer.Deserialize<string[]>(json);
            
            if (keys != null && keys.Length > 0 && serfConfig.MemberlistConfig != null)
            {
                // Use first key as primary encryption key
                serfConfig.MemberlistConfig.SecretKey = Convert.FromBase64String(keys[0]);
                logger?.LogInformation("[Agent] Loaded keyring from {File} ({Count} keys)",
                                      keyringFile, keys.Length);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[Agent] Failed to load keyring from {File}", keyringFile);
            throw;
        }
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Gracefully leave the cluster.
    /// Ported from Go: Leave()
    /// </summary>
    public async Task LeaveAsync()
    {
        _logger?.LogInformation("[Agent] Requesting graceful leave from Serf");
        await _serf.LeaveAsync();
    }

    /// <summary>
    /// Shutdown the agent and all resources.
    /// Ported from Go: Shutdown()
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _logger?.LogInformation("[Agent] Shutting down agent");

        // Signal shutdown
        _shutdownCts.Cancel();

        // Wait for event loop to finish
        if (_eventLoopTask != null)
        {
            try
            {
                await _eventLoopTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        // Shutdown Serf
        if (_serf != null)
        {
            _serf.Dispose();
        }
        
        _shutdownCts.Dispose();
        _disposed = true;

        _logger?.LogInformation("[Agent] Shutdown complete");
    }

    #endregion
}
