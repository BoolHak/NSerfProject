// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Serf;
using NSerf.Serf.Events;
using NSerf.Agent.RPC;
using System.Text.Json;
using System.Threading.Channels;

namespace NSerf.Agent;

public class SerfAgent : IAsyncDisposable
{
    private readonly AgentConfig _config;
    private readonly ILogger? _logger;
    private readonly HashSet<IEventHandler> _eventHandlers = new();
    private readonly object _eventHandlersLock = new();
    private IEventHandler[] _eventHandlerList = Array.Empty<IEventHandler>();
    private readonly Channel<Event> _eventChannel;
    private readonly CancellationTokenSource _cts = new();
    private Serf.Serf? _serf;
    private RpcServer? _rpcServer;
    private ScriptEventHandler? _scriptEventHandler;
    private Task? _eventLoopTask;
    private bool _disposed;
    private bool _started;
    private readonly SemaphoreSlim _shutdownLock = new(1, 1);

    /// <summary>
    /// Circular log writer for monitor command.
    /// </summary>
    public CircularLogWriter? LogWriter { get; private set; }

    public SerfAgent(AgentConfig config, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        
        // Validate mutual exclusions
        if (_config.Tags.Count > 0 && !string.IsNullOrEmpty(_config.TagsFile))
        {
            throw new ConfigException("Tags config not allowed while using tag files");
        }
        
        if (!string.IsNullOrEmpty(_config.EncryptKey) && !string.IsNullOrEmpty(_config.KeyringFile))
        {
            throw new ConfigException("Encryption key not allowed while using a keyring");
        }
        
        // Create event channel (size=64 as per Go implementation)
        _eventChannel = Channel.CreateBounded<Event>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        // Create circular log writer (buffer size=512 as per Go implementation)
        LogWriter = new CircularLogWriter(512);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            throw new InvalidOperationException("Agent already started");
        
        if (_disposed)
            throw new InvalidOperationException("Agent has been disposed");
        
        _started = true;
        _logger?.LogInformation("[Agent] Starting...");

        // Load tags from file if specified
        if (!string.IsNullOrEmpty(_config.TagsFile))
        {
            await LoadTagsFileAsync();
        }
        
        var serfConfig = BuildSerfConfig();
        serfConfig.EventCh = _eventChannel.Writer;
        
        _serf = await NSerf.Serf.Serf.CreateAsync(serfConfig);
        
        // Setup script event handler if configured
        if (_config.EventHandlers != null && _config.EventHandlers.Count > 0)
        {
            var scripts = new List<EventScript>();
            foreach (var handlerSpec in _config.EventHandlers)
            {
                scripts.AddRange(EventScript.Parse(handlerSpec));
            }
            
            _scriptEventHandler = new ScriptEventHandler(
                () => _serf.LocalMember(),
                scripts.ToArray(),
                _logger);
            
            RegisterEventHandler(_scriptEventHandler);
        }
        
        // Start event loop
        _eventLoopTask = Task.Run(() => EventLoopAsync(), _cts.Token);

        if (_config.StartJoin.Length > 0)
        {
            var ignoreOld = true;  // Default: don't replay old events
            var joined = await _serf.JoinAsync(_config.StartJoin, ignoreOld);
            if (joined == 0)
            {
                _logger?.LogWarning("[Agent] Failed to join any nodes");
            }
            else
            {
                _logger?.LogInformation("[Agent] Joined {Count} nodes", joined);
            }
        }

        // Start RPC server if RPCAddr is configured
        if (!string.IsNullOrEmpty(_config.RPCAddr))
        {
            _rpcServer = new RpcServer(this, _config.RPCAddr, _config.RPCAuthKey);
            await _rpcServer.StartAsync(cancellationToken);
        }
        
        _logger?.LogInformation("[Agent] Started successfully");
    }

    private Config BuildSerfConfig()
    {
        var config = new Config
        {
            MemberlistConfig = NSerf.Memberlist.Configuration.MemberlistConfig.DefaultLANConfig(),
            Tags = new Dictionary<string, string>(_config.Tags),  // Copy tags from config
            ProtocolVersion = (byte)_config.Protocol
        };
        
        if (!string.IsNullOrEmpty(_config.NodeName))
        {
            config.NodeName = _config.NodeName;
            if (config.MemberlistConfig != null)
            {
                config.MemberlistConfig.Name = _config.NodeName;
            }
        }
            
        if (!string.IsNullOrEmpty(_config.BindAddr) && config.MemberlistConfig != null)
        {
            // Parse "IP:Port" format
            var parts = _config.BindAddr.Split(':');
            if (parts.Length == 2)
            {
                config.MemberlistConfig.BindAddr = parts[0];
                if (int.TryParse(parts[1], out var port))
                {
                    config.MemberlistConfig.BindPort = port;
                }
            }
            else
            {
                config.MemberlistConfig.BindAddr = _config.BindAddr;
            }
        }
        
        return config;
    }

    public NSerf.Serf.Serf? Serf => _serf;

    /// <summary>
    /// Registers an event handler. Uses handler list rebuild pattern to prevent deadlocks.
    /// </summary>
    public void RegisterEventHandler(IEventHandler handler)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        
        lock (_eventHandlersLock)
        {
            _eventHandlers.Add(handler);
            // Rebuild list for lock-free iteration
            _eventHandlerList = _eventHandlers.ToArray();
        }
        
        _logger?.LogDebug("[Agent] Event handler registered");
    }

    /// <summary>
    /// Deregisters an event handler.
    /// </summary>
    public void DeregisterEventHandler(IEventHandler handler)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        
        lock (_eventHandlersLock)
        {
            _eventHandlers.Remove(handler);
            // Rebuild list for lock-free iteration
            _eventHandlerList = _eventHandlers.ToArray();
        }
        
        _logger?.LogDebug("[Agent] Event handler deregistered");
    }

    /// <summary>
    /// Event loop that dispatches events to registered handlers.
    /// Monitors Serf shutdown and triggers agent shutdown if Serf dies.
    /// </summary>
    private async Task EventLoopAsync()
    {
        try
        {
            while (!_disposed && !_cts.Token.IsCancellationRequested)
            {
                // Monitor for events or Serf shutdown
                var eventRead = _eventChannel.Reader.ReadAsync(_cts.Token);
                
                Event evt;
                try
                {
                    evt = await eventRead;
                }
                catch (ChannelClosedException)
                {
                    // Serf shutdown detected
                    _logger?.LogWarning("[Agent] Serf shutdown detected, triggering agent shutdown");
                    await ShutdownAsync();
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                
                DispatchEvent(evt);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Agent] Event loop error");
        }
    }

    /// <summary>
    /// Dispatches event to all registered handlers. Handler exceptions don't stop the loop.
    /// </summary>
    private void DispatchEvent(Event evt)
    {
        IEventHandler[] handlers;
        lock (_eventHandlersLock)
        {
            handlers = _eventHandlerList;  // Snapshot for lock-free iteration
        }
        
        foreach (var handler in handlers)
        {
            try
            {
                handler.HandleEvent(evt);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[Agent] Event handler threw exception");
            }
        }
    }

    /// <summary>
    /// Updates the local node's tags. Persists to file BEFORE gossiping (critical ordering).
    /// </summary>
    public async Task SetTagsAsync(Dictionary<string, string> tags)
    {
        if (_serf == null)
            throw new InvalidOperationException("Agent not started");
        
        // Persist to file BEFORE gossiping (critical ordering from Go implementation)
        if (!string.IsNullOrEmpty(_config.TagsFile))
        {
            await WriteTagsFileAsync(tags);
        }
        
        // Now gossip the tags
        await _serf.SetTagsAsync(tags);
    }

    private async Task LoadTagsFileAsync()
    {
        if (string.IsNullOrEmpty(_config.TagsFile))
            return;
        
        if (!File.Exists(_config.TagsFile))
        {
            _logger?.LogDebug("[Agent] Tags file does not exist: {File}", _config.TagsFile);
            return;
        }
        
        var json = await File.ReadAllTextAsync(_config.TagsFile);
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger?.LogDebug("[Agent] Tags file is empty: {File}", _config.TagsFile);
            return;
        }
        
        var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        
        if (tags != null && tags.Count > 0)
        {
            // Merge into config tags
            foreach (var kvp in tags)
            {
                _config.Tags[kvp.Key] = kvp.Value;
            }
            
            _logger?.LogInformation("[Agent] Loaded {Count} tags from file", tags.Count);
        }
    }

    private async Task WriteTagsFileAsync(Dictionary<string, string> tags)
    {
        var json = JsonSerializer.Serialize(tags, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        
        await File.WriteAllTextAsync(_config.TagsFile!, json);
        
        _logger?.LogDebug("[Agent] Wrote tags to file: {File}", _config.TagsFile);
    }

    /// <summary>
    /// Parses CLI tag arguments in "key=value" format.
    /// </summary>
    public static Dictionary<string, string> UnmarshalTags(string[] tags)
    {
        var result = new Dictionary<string, string>();
        
        foreach (var tag in tags)
        {
            var parts = tag.Split('=', 2);
            if (parts.Length != 2 || string.IsNullOrEmpty(parts[0]))
            {
                throw new FormatException($"Invalid tag: '{tag}'");
            }
            
            result[parts[0]] = parts[1];
        }
        
        return result;
    }

    /// <summary>
    /// Gracefully shutdown the agent. Idempotent - can be called multiple times.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (_disposed)
            return;  // Already shutdown
        
        await _shutdownLock.WaitAsync();
        try
        {
            if (_disposed)
                return;  // Double-check after acquiring lock
            
            _logger?.LogInformation("[Agent] Shutting down...");
            _disposed = true;
            
            // Shutdown RPC server
            if (_rpcServer != null)
            {
                await _rpcServer.DisposeAsync();
                _rpcServer = null;
            }
            
            // Shutdown Serf (triggers event channel close)
            if (_serf != null)
            {
                await _serf.ShutdownAsync();
                _serf = null;
            }
            
            // Cancel event loop
            _cts.Cancel();
            
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
            
            _logger?.LogInformation("[Agent] Shutdown complete");
        }
        finally
        {
            _shutdownLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
        
        // Dispose resources after shutdown completes
        _cts?.Dispose();
        _shutdownLock?.Dispose();
    }
}
