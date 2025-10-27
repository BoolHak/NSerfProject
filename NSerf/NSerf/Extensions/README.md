# NSerf Dependency Injection Extensions

This package provides ASP.NET Core integration for NSerf, allowing you to easily add Serf cluster membership to your applications using the standard .NET dependency injection container.

## Installation

The extensions are included in the main `NSerf` package. No additional packages are required.

## Quick Start

### Basic Usage

Add Serf to your application with default settings:

```csharp
using NSerf.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Serf with default configuration
builder.Services.AddSerf();

var app = builder.Build();
app.Run();
```

### Custom Configuration

Configure Serf using the fluent options API:

```csharp
builder.Services.AddSerf(options =>
{
    options.NodeName = "web-server-1";
    options.BindAddr = "0.0.0.0:7946";
    options.Tags["role"] = "web";
    options.Tags["datacenter"] = "us-east-1";
    options.Tags["version"] = "1.0.0";
    options.StartJoin = new[] { "10.0.1.10:7946", "10.0.1.11:7946" };
    options.SnapshotPath = "/var/serf/snapshot";
    options.RejoinAfterLeave = true;
    options.RPCAddr = "127.0.0.1:7373";
});
```

### Configuration from appsettings.json

Use configuration binding for environment-specific settings:

**appsettings.json:**
```json
{
  "Serf": {
    "NodeName": "web-server-1",
    "BindAddr": "0.0.0.0:7946",
    "Tags": {
      "role": "web",
      "datacenter": "us-east-1",
      "environment": "production"
    },
    "StartJoin": ["10.0.1.10:7946", "10.0.1.11:7946"],
    "RetryJoin": ["10.0.1.10:7946", "10.0.1.11:7946", "10.0.1.12:7946"],
    "RetryInterval": "00:00:30",
    "SnapshotPath": "/var/serf/snapshot",
    "RejoinAfterLeave": true,
    "DisableCoordinates": false,
    "EventHandlers": [
      "member-join=/usr/local/bin/member-join.sh",
      "member-leave=/usr/local/bin/member-leave.sh",
      "user:deploy=/usr/local/bin/deploy.sh"
    ]
  }
}
```

**Program.cs:**
```csharp
builder.Services.AddSerf(builder.Configuration, "Serf");
```

## Using Serf in Your Services

Inject `SerfAgent` or `Serf` into your services:

```csharp
using NSerf.Agent;
using NSerf.Serf;

public class ClusterService
{
    private readonly SerfAgent _agent;
    private readonly Serf _serf;

    public ClusterService(SerfAgent agent, Serf serf)
    {
        _agent = agent;
        _serf = serf;
    }

    public async Task<int> GetClusterSizeAsync()
    {
        var members = _serf.Members();
        return members.Length;
    }

    public async Task BroadcastDeploymentAsync(string version)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(version);
        await _serf.UserEventAsync("deploy", payload, coalesce: true);
    }

    public async Task<string[]> QueryHealthAsync()
    {
        var responses = new List<string>();
        var query = await _serf.QueryAsync(
            name: "health",
            payload: null,
            filterNodes: null,
            filterTags: null,
            requestAck: true,
            timeout: TimeSpan.FromSeconds(5)
        );

        await foreach (var response in query.ResponseChannel.Reader.ReadAllAsync())
        {
            var payload = System.Text.Encoding.UTF8.GetString(response.Payload);
            responses.Add($"{response.From}: {payload}");
        }

        return responses.ToArray();
    }
}

// Register in DI
builder.Services.AddSingleton<ClusterService>();
```

## Event Handling

Register custom event handlers to react to cluster events:

```csharp
using NSerf.Agent;
using NSerf.Serf.Events;

public class CustomEventHandler : IEventHandler
{
    private readonly ILogger<CustomEventHandler> _logger;

    public CustomEventHandler(ILogger<CustomEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleEventAsync(Event evt)
    {
        switch (evt)
        {
            case MemberEvent memberEvent:
                foreach (var member in memberEvent.Members)
                {
                    _logger.LogInformation(
                        "Member {Name} is now {Status}",
                        member.Name,
                        memberEvent.Type);
                }
                break;

            case UserEvent userEvent:
                _logger.LogInformation(
                    "User event {Name} from {From}",
                    userEvent.Name,
                    userEvent.LTime);
                break;

            case Query query:
                _logger.LogInformation("Query {Name} received", query.Name);
                // Respond to query
                await query.RespondAsync(
                    System.Text.Encoding.UTF8.GetBytes("healthy"));
                break;
        }
    }
}

// Register and attach handler
builder.Services.AddSingleton<CustomEventHandler>();

// In a startup service or hosted service:
public class EventHandlerRegistration : IHostedService
{
    private readonly SerfAgent _agent;
    private readonly CustomEventHandler _handler;

    public EventHandlerRegistration(SerfAgent agent, CustomEventHandler handler)
    {
        _agent = agent;
        _handler = handler;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _agent.RegisterEventHandler(_handler);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _agent.DeregisterEventHandler(_handler);
        return Task.CompletedTask;
    }
}

builder.Services.AddHostedService<EventHandlerRegistration>();
```

## Configuration Options

### SerfOptions Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `NodeName` | `string` | Machine name | Unique node identifier |
| `BindAddr` | `string` | `"0.0.0.0:7946"` | Bind address (IP:Port) |
| `AdvertiseAddr` | `string?` | `null` | Advertise address for NAT |
| `EncryptKey` | `string?` | `null` | Base64 encryption key (32 bytes) |
| `KeyringFile` | `string?` | `null` | Path to keyring file |
| `RPCAddr` | `string?` | `null` | RPC server address |
| `RPCAuthKey` | `string?` | `null` | RPC authentication key |
| `Tags` | `Dictionary<string, string>` | Empty | Node metadata tags |
| `TagsFile` | `string?` | `null` | Path to tags file |
| `Profile` | `string` | `"lan"` | Network profile (lan/wan/local) |
| `SnapshotPath` | `string?` | `null` | Snapshot file path |
| `Protocol` | `int` | `5` | Serf protocol version |
| `RejoinAfterLeave` | `bool` | `false` | Allow rejoin after leave |
| `ReplayOnJoin` | `bool` | `false` | Replay events on join |
| `StartJoin` | `string[]` | Empty | Nodes to join on startup |
| `RetryJoin` | `string[]` | Empty | Nodes to retry joining |
| `RetryInterval` | `TimeSpan` | 30s | Retry join interval |
| `RetryMaxAttempts` | `int` | `0` | Max retry attempts (0=unlimited) |
| `DisableCoordinates` | `bool` | `false` | Disable network coordinates |
| `EventHandlers` | `List<string>` | Empty | Event handler scripts |
| `ReconnectInterval` | `TimeSpan` | 60s | Failed node reconnect interval |
| `ReconnectTimeout` | `TimeSpan` | 72h | Reconnect timeout |
| `TombstoneTimeout` | `TimeSpan` | 24h | Left node tombstone timeout |
| `LeaveOnTerm` | `bool` | `true` | Leave on SIGTERM |
| `SkipLeaveOnInt` | `bool` | `false` | Skip leave on SIGINT |

## Lifecycle Management

The `SerfHostedService` automatically manages the Serf agent lifecycle:

- **Startup**: Agent starts when the application starts
- **Shutdown**: Agent gracefully leaves the cluster and shuts down when the application stops

You can also manually control the agent:

```csharp
public class ManualControlService
{
    private readonly SerfAgent _agent;

    public ManualControlService(SerfAgent agent)
    {
        _agent = agent;
    }

    public async Task ForceLeaveNodeAsync(string nodeName)
    {
        if (_agent.Serf != null)
        {
            await _agent.Serf.RemoveFailedNodeAsync(nodeName);
        }
    }

    public async Task UpdateTagsAsync(Dictionary<string, string> newTags)
    {
        await _agent.SetTagsAsync(newTags);
    }
}
```

## Health Checks

Integrate Serf with ASP.NET Core health checks:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

public class SerfHealthCheck : IHealthCheck
{
    private readonly Serf _serf;

    public SerfHealthCheck(Serf serf)
    {
        _serf = serf;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var state = _serf.State;
            var members = _serf.Members();
            var aliveCount = members.Count(m => m.Status == MemberStatus.Alive);

            var data = new Dictionary<string, object>
            {
                ["state"] = state.ToString(),
                ["members"] = members.Length,
                ["alive"] = aliveCount
            };

            return Task.FromResult(
                state == SerfState.Alive
                    ? HealthCheckResult.Healthy("Serf cluster is healthy", data)
                    : HealthCheckResult.Degraded($"Serf state is {state}", data: data)
            );
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Serf cluster check failed", ex)
            );
        }
    }
}

// Register health check
builder.Services.AddHealthChecks()
    .AddCheck<SerfHealthCheck>("serf");
```

## Best Practices

1. **Use Snapshots**: Enable `SnapshotPath` for production to persist cluster state across restarts
2. **Set Unique Node Names**: Ensure each node has a unique `NodeName` in the cluster
3. **Configure Retry Join**: Use `RetryJoin` for resilient cluster formation
4. **Tag Your Nodes**: Use `Tags` for service discovery and routing
5. **Enable RPC Carefully**: Only expose `RPCAddr` on trusted networks or use `RPCAuthKey`
6. **Handle Events Asynchronously**: Event handlers should not block
7. **Monitor Cluster Health**: Integrate with health checks and metrics

## Troubleshooting

### Agent Fails to Start

Check logs for configuration errors. Common issues:
- Port already in use (change `BindAddr`)
- Invalid encryption key (must be base64-encoded 32 bytes)
- Cannot reach `StartJoin` nodes (check network connectivity)

### Nodes Not Joining

- Verify `BindAddr` and `AdvertiseAddr` are correct
- Check firewall rules (default port 7946 UDP/TCP)
- Ensure encryption keys match across cluster
- Review `RetryJoin` configuration

### Events Not Firing

- Verify event handlers are registered before agent starts
- Check handler exceptions in logs
- Ensure event channel is not blocked
