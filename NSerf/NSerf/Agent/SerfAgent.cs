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
    private IEventHandler[] _eventHandlerList = [];
    private readonly Channel<Event> _eventChannel;
    private readonly CancellationTokenSource _cts = new();
    private Serf.Serf? _serf;
    private RpcServer? _rpcServer;
    private ScriptEventHandler? _scriptEventHandler;
    private Task? _eventLoopTask;
    private Task? _retryJoinTask;
    private bool _disposed;
    private bool _started;
    private readonly SemaphoreSlim _shutdownLock = new(1, 1);
    private NSerf.Memberlist.Keyring? _loadedKeyring;

    /// <summary>
    /// Circular log writer for monitor command.
    /// </summary>
    public CircularLogWriter? LogWriter { get; private set; }

    /// <summary>
    /// Gets the node name for this agent.
    /// </summary>
    public string NodeName => _config.NodeName;

    /// <summary>
    /// Gets the agent configuration.
    /// </summary>
    public AgentConfig Config => _config;
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true
    };

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

        // Load keyring from file if specified
        if (!string.IsNullOrEmpty(_config.KeyringFile))
        {
            await LoadKeyringFileAsync();
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
                [.. scripts],
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

        // Start retry join in background if configured
        if (_config.RetryJoin != null && _config.RetryJoin.Length > 0)
        {
            _retryJoinTask = Task.Run(() => RetryJoinAsync(_cts.Token), _cts.Token);
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
        Config config = BuildConfig();

        SetNodeName(config);
        SetBind(config);
        SetAdvertise(config);
        SetKeys(config);

        return config;
    }

    private void SetKeys(Config config)
    {
        // Set keyring if loaded from file
        if (_loadedKeyring != null && config.MemberlistConfig != null)
        {
            config.MemberlistConfig.Keyring = _loadedKeyring;
            config.KeyringFile = _config.KeyringFile;  // So Serf can write updates
            _logger?.LogDebug("[Agent] Configured keyring from file");
        }
        // Or set encryption key if provided directly
        else if (!string.IsNullOrEmpty(_config.EncryptKey) && config.MemberlistConfig != null)
        {
            var keyBytes = _config.EncryptBytes();
            if (keyBytes != null)
            {
                config.MemberlistConfig.Keyring = NSerf.Memberlist.Keyring.Create(null, keyBytes);
                _logger?.LogDebug("[Agent] Configured keyring from EncryptKey");
            }
        }
    }

    private void SetAdvertise(Config config)
    {
        // Set advertise address if specified (for NAT/specific network configs)
        if (!string.IsNullOrEmpty(_config.AdvertiseAddr) && config.MemberlistConfig != null)
        {
            var parts = _config.AdvertiseAddr.Split(':');
            if (parts.Length == 2)
            {
                config.MemberlistConfig.AdvertiseAddr = parts[0];
                if (int.TryParse(parts[1], out var port))
                {
                    config.MemberlistConfig.AdvertisePort = port;
                }
            }
            else
            {
                config.MemberlistConfig.AdvertiseAddr = _config.AdvertiseAddr;
            }
        }
    }

    private void SetBind(Config config)
    {
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
    }

    private Config BuildConfig()
    {
        var config = new Config
        {
            MemberlistConfig = NSerf.Memberlist.Configuration.MemberlistConfig.DefaultLANConfig(),
            Tags = new Dictionary<string, string>(_config.Tags),  // Copy tags from config
            ProtocolVersion = (byte)_config.Protocol,
            DisableCoordinates = _config.DisableCoordinates,
            SnapshotPath = _config.SnapshotPath,  // CRITICAL: Pass snapshot path to Serf
            RejoinAfterLeave = _config.RejoinAfterLeave  // CRITICAL: Pass rejoin flag to Serf
        };
        return config;
    }

    private void SetNodeName(Config config)
    {
        if (!string.IsNullOrEmpty(_config.NodeName))
        {
            config.NodeName = _config.NodeName;
            if (config.MemberlistConfig != null)
            {
                config.MemberlistConfig.Name = _config.NodeName;
            }
        }
    }

    public NSerf.Serf.Serf? Serf => _serf;

    /// <summary>
    /// Registers an event handler. Uses handler list rebuild pattern to prevent deadlocks.
    /// </summary>
    public void RegisterEventHandler(IEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_eventHandlersLock)
        {
            _eventHandlers.Add(handler);
            // Rebuild list for lock-free iteration
            _eventHandlerList = [.. _eventHandlers];
        }

        _logger?.LogDebug("[Agent] Event handler registered");
    }

    /// <summary>
    /// Deregisters an event handler.
    /// </summary>
    public void DeregisterEventHandler(IEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_eventHandlersLock)
        {
            _eventHandlers.Remove(handler);
            // Rebuild list for lock-free iteration
            _eventHandlerList = [.. _eventHandlers];
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
                catch (ChannelClosedException ex)
                {
                    // Serf shutdown detected
                    _logger?.LogWarning(ex, "[Agent] Serf shutdown detected, triggering agent shutdown");
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
        var json = JsonSerializer.Serialize(tags, _jsonSerializerOptions);

        await File.WriteAllTextAsync(_config.TagsFile!, json);

        _logger?.LogDebug("[Agent] Wrote tags to file: {File}", _config.TagsFile);
    }

    private async Task LoadKeyringFileAsync()
    {
        if (string.IsNullOrEmpty(_config.KeyringFile))
            return;

        if (!File.Exists(_config.KeyringFile))
        {
            _logger?.LogWarning("[Agent] Keyring file does not exist: {File}", _config.KeyringFile);
            return;
        }

        var json = await File.ReadAllTextAsync(_config.KeyringFile);
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger?.LogWarning("[Agent] Keyring file is empty: {File}", _config.KeyringFile);
            return;
        }

        var keysBase64 = JsonSerializer.Deserialize<string[]>(json);
        if (keysBase64 == null || keysBase64.Length == 0)
        {
            _logger?.LogWarning("[Agent] No keys found in keyring file: {File}", _config.KeyringFile);
            return;
        }

        // Decode base64 keys
        var keys = new List<byte[]>();
        foreach (var keyBase64 in keysBase64)
        {
            try
            {
                var keyBytes = Convert.FromBase64String(keyBase64);
                keys.Add(keyBytes);
            }
            catch (FormatException ex)
            {
                _logger?.LogError(ex, "[Agent] Invalid base64 key in keyring file: {Key}", keyBase64);
                throw new ConfigException($"Invalid base64 key in keyring file: {keyBase64}");
            }
        }

        // Create keyring with first key as primary
        if (keys.Count > 0)
        {
            // First key is primary, rest are secondary
            var secondaryKeys = keys.Count > 1 ? keys.Skip(1).ToArray() : null;
            var keyring = NSerf.Memberlist.Keyring.Create(secondaryKeys, keys[0]);

            // Store keyring to be used when building Serf config
            _loadedKeyring = keyring;

            _logger?.LogInformation("[Agent] Loaded {Count} keys from keyring file", keys.Count);
        }
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

    public void UpdateEventHandlers(List<string> handlerSpecs)
    {
        ArgumentNullException.ThrowIfNull(handlerSpecs);

        var scripts = new List<EventScript>();
        foreach (var spec in handlerSpecs)
        {
            scripts.AddRange(EventScript.Parse(spec));
        }

        if (_scriptEventHandler == null)
        {
            if (scripts.Count == 0)
                return;

            if (_serf == null)
                throw new InvalidOperationException("Agent not started");

            _scriptEventHandler = new ScriptEventHandler(
                () => _serf.LocalMember(),
                [.. scripts],
                _logger);
            RegisterEventHandler(_scriptEventHandler);
        }
        else
        {
            _scriptEventHandler.UpdateScripts([.. scripts]);
        }
    }

    /// <summary>
    /// RetryJoinAsync attempts to join configured addresses with retries.
    /// Runs in background until successful or max attempts reached.
    /// </summary>
    private async Task RetryJoinAsync(CancellationToken cancellationToken)
    {
        if (_serf == null || _config.RetryJoin == null || _config.RetryJoin.Length == 0)
            return;

        var attempt = 0;
        var maxAttempts = _config.RetryMaxAttempts;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            bool flowControl = await TryJoin(attempt);
            if (!flowControl)
            {
                return;
            }

            // Check if we've hit max attempts
            if (maxAttempts > 0 && attempt >= maxAttempts)
            {
                _logger?.LogWarning("[Agent] Retry join failed after {Attempts} attempts", attempt);
                return;
            }

            // Wait before next attempt
            try
            {
                await Task.Delay(_config.RetryInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return; // Shutdown requested
            }
        }
    }

    private async Task<bool> TryJoin(int attempt)
    {
        try
        {
            if (_serf != null && _config.RetryJoin != null)
            {
                var joined = await _serf.JoinAsync(_config.RetryJoin, !_config.ReplayOnJoin);
                if (joined > 0)
                {
                    _logger?.LogInformation("[Agent] Retry join succeeded, joined {Count} nodes", joined);
                    return false; // Success - exit retry loop
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Agent] Retry join attempt {Attempt} failed: {Message}", attempt, ex.Message);
        }

        return true;
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

            // Gracefully leave cluster before shutdown (broadcasts leave event)
            if (_serf != null)
            {
                try
                {
                    // Attempt graceful leave with timeout to avoid hanging on shutdown
                    // Timeout = BroadcastTimeout (5s) + LeavePropagateDelay (1s) + buffer (4s) = 10s
                    var leaveTask = _serf.LeaveAsync();
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                    var completedTask = await Task.WhenAny(leaveTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        _logger?.LogWarning("[Agent] Leave timed out after 10 seconds, forcing shutdown");
                    }
                    else
                    {
                        _logger?.LogInformation("[Agent] Successfully left cluster");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[Agent] Error during leave, forcing shutdown");
                }

                // Shutdown Serf (triggers event channel close)
                await _serf.ShutdownAsync();
                _serf = null;
            }

            // Cancel event loop and retry join
            await _cts.CancelAsync();

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

            // Wait for retry join to finish
            if (_retryJoinTask != null)
            {
                try
                {
                    await _retryJoinTask;
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
        GC.SuppressFinalize(this);
    }
}
