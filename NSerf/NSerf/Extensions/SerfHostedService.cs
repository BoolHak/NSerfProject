// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NSerf.Extensions;

/// <summary>
/// Hosted service that manages the Serf agent lifecycle within ASP.NET Core applications.
/// Starts the agent on application startup and gracefully shuts it down on application stop.
/// </summary>
internal sealed class SerfHostedService : IHostedService
{
    private readonly Agent.SerfAgent _agent;
    private readonly ILogger<SerfHostedService> _logger;

    public SerfHostedService(
        Agent.SerfAgent agent,
        ILogger<SerfHostedService> logger)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Serf agent: {NodeName}", _agent.NodeName);

        try
        {
            await _agent.StartAsync(cancellationToken);
            _logger.LogInformation("Serf agent started successfully");
        }
        catch (Exception ex)
        {
            const string message = "Failed to start Serf agent";
            _logger.LogError(ex, message);
            throw new InvalidOperationException(message, ex);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Serf agent: {NodeName}", _agent.NodeName);

        try
        {
            await _agent.ShutdownAsync();
            _logger.LogInformation("Serf agent stopped successfully");
        }
        catch (Exception ex)
        {
            var message = $"Error during Serf agent shutdown for '{_agent.NodeName}'";
            _logger.LogWarning(ex, message);
            throw new InvalidOperationException(message, ex);
        }
    }
}
