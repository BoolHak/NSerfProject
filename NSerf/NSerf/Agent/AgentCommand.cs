// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace NSerf.Agent;

/// <summary>
/// Main agent command with full lifecycle management.
/// Maps to: Go's command.go
/// </summary>
public class AgentCommand : IAsyncDisposable
{
    private readonly AgentConfig _config;
    private readonly ILogger? _logger;
    private readonly SignalHandler _signalHandler = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly TaskCompletionSource<int> _exitCodeTcs = new();

    private SerfAgent? _agent;
    private RPC.RpcServer? _rpcServer;
    private Task? _retryJoinTask;
    private GatedWriter? _gatedWriter;
    private LogWriter? _logWriter;
    private int _signalCount;
    private readonly object _shutdownLock = new();

    private const int GracefulTimeoutSeconds = 3;
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    public AgentCommand(AgentConfig config, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;

        // Setup signal handling
        _signalHandler.RegisterCallback(HandleSignal);
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Setup log writers
            var consoleOutput = Console.Out;
            _gatedWriter = new GatedWriter(consoleOutput);
            _logWriter = new LogWriter(_gatedWriter, LogLevelExtensions.FromString(_config.LogLevel ?? "INFO"));

            // Redirect console output through log writer
            Console.SetOut(_logWriter);

            _logger?.LogInformation("[Agent] Starting Serf agent Node name: {NodeName} Bind addr: {BindAddr}", _config.NodeName, _config.BindAddr);


            // Create and start agent
            _agent = new SerfAgent(_config, _logger);
            await _agent.StartAsync(cancellationToken);

            // Release buffered logs now that startup succeeded
            await _gatedWriter.FlushAsync(cancellationToken);

            // Start RPC server if configured
            if (!string.IsNullOrEmpty(_config.RPCAddr))
            {
                _rpcServer = new RPC.RpcServer(_agent, _config.RPCAddr, _config.RPCAuthKey);
                await _rpcServer.StartAsync(cancellationToken);
                _logger?.LogInformation("[Agent] RPC server listening on {RPCAddr}", _config.RPCAddr);
            }

            // Start join if configured
            if (_config.StartJoin.Length > 0 && _agent.Serf != null)
            {
                var joined = await _agent.Serf.JoinAsync(_config.StartJoin, !_config.ReplayOnJoin);
                if (joined == 0)
                {
                    _logger?.LogWarning("[Agent] Failed to join any nodes from start_join");
                }
                else
                {
                    _logger?.LogInformation("[Agent] Joined {Count} nodes", joined);
                }
            }

            // Start retry join in background if configured
            if (_config.RetryJoin != null && _config.RetryJoin.Length > 0)
            {
                _retryJoinTask = Task.Run(() => RetryJoinAsync(_shutdownCts.Token), _shutdownCts.Token);
            }

            _logger?.LogInformation("[Agent] Serf agent running!");

            // Wait for shutdown signal or agent shutdown
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);

            try
            {
                await _exitCodeTcs.Task.WaitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }

            return _exitCodeTcs.Task.IsCompleted ? _exitCodeTcs.Task.Result : 0;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Agent] Failed to start agent");
            return 1;
        }
    }

    private void HandleSignal(Signal signal)
    {
        lock (_shutdownLock)
        {
            _signalCount++;

            _logger?.LogInformation("[Agent] Received signal: {Signal}", signal);

            // SIGHUP triggers config reload
            if (signal == Signal.SIGHUP)
            {
                _logger?.LogInformation("[Agent] Reloading configuration...");
                _ = Task.Run(() => ReloadConfigAsync());
                return;
            }

            // First signal: graceful shutdown (if configured)
            if (_signalCount == 1)
            {
                bool shouldGraceful = signal == Signal.SIGINT && !_config.SkipLeaveOnInt || signal == Signal.SIGTERM && _config.LeaveOnTerm;

                if (shouldGraceful)
                {
                    _logger?.LogInformation("[Agent] Gracefully shutting down agent...");
                    _ = Task.Run(() => GracefulShutdownAsync());
                }
                else
                {
                    _logger?.LogInformation("[Agent] Forcing shutdown...");
                    ForceShutdown();
                }
            }
            // Second signal: force shutdown
            else if (_signalCount >= 2)
            {
                _logger?.LogWarning("[Agent] Force shutdown due to second signal");
                ForceShutdown();
            }
        }
    }

    private async Task GracefulShutdownAsync()
    {
        try
        {
            var timeout = TimeSpan.FromSeconds(GracefulTimeoutSeconds);
            using var cts = new CancellationTokenSource(timeout);

            if (_agent != null)
            {
                if (_agent.Serf != null)
                {
                    await _agent.Serf.LeaveAsync();
                }
                await _agent.ShutdownAsync();
            }

            _exitCodeTcs.TrySetResult(0);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Agent] Error during graceful shutdown");
            ForceShutdown();
        }
    }

    private void ForceShutdown()
    {
        _shutdownCts.Cancel();
        _exitCodeTcs.TrySetResult(1);
    }

    private async Task RetryJoinAsync(CancellationToken cancellationToken)
    {
        if (!IsAbleToRetry()) return;

        var interval = GetRetryInterval();
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;

            try
            {
                if (_agent!.Serf != null && _config.RetryJoin != null)
                {
                    var joined = await _agent.Serf.JoinAsync(_config.RetryJoin, !_config.ReplayOnJoin);
                    if (joined > 0)
                    {
                        _logger?.LogInformation("[Agent] Retry join succeeded, joined {Count} nodes", joined);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Agent] Retry join attempt {Attempt} failed", attempt);
            }

            // Check max attempts
            if (_config.RetryMaxAttempts > 0 && attempt >= _config.RetryMaxAttempts)
            {
                _logger?.LogError("[Agent] Max retry attempts ({Max}) reached, giving up", _config.RetryMaxAttempts);
                ForceShutdown();
                return;
            }

            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private bool IsAbleToRetry() =>
        _agent != null && _agent.Serf != null &&
        _config.RetryJoin != null && _config.RetryJoin.Length > 0;

    private TimeSpan GetRetryInterval() =>
        _config.RetryInterval > MinInterval ? _config.RetryInterval : MinInterval;

    private Task ReloadConfigAsync()
    {
        try
        {
            _logger?.LogInformation("[Agent] Config reload triggered");

            // For now, only log level and event scripts can be reloaded
            // Full implementation would reload from config file

            // Update log level
            if (!string.IsNullOrEmpty(_config.LogLevel) && _logWriter != null)
            {
                var newLevel = LogLevelExtensions.FromString(_config.LogLevel);
                // Create new log writer with updated level
                var consoleOutput = Console.Out;
                _logWriter = new LogWriter(consoleOutput, newLevel);
                Console.SetOut(_logWriter);
                _logger?.LogInformation("[Agent] Log level updated");
            }

            // Update event scripts (if ScriptEventHandler supports hot reload)
            _logger?.LogInformation("[Agent] Config reload completed");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Agent] Failed to reload config");
            return Task.CompletedTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdownCts.CancelAsync();

        // Wait for retry join task to complete after cancellation
        if (_retryJoinTask != null)
        {
            try
            {
                await _retryJoinTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when shutdown token is canceled
            }
        }

        if (_rpcServer != null)
        {
            await _rpcServer.DisposeAsync();
        }

        if (_agent != null)
        {
            await _agent.DisposeAsync();
        }

        _signalHandler?.Dispose();
        _shutdownCts?.Dispose();

        // Restore console output
        if (_gatedWriter != null)
        {
            Console.SetOut(Console.Out);
            await _gatedWriter.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}
