# NSerf - HashiCorp Serf for .NET

A complete C# port of [HashiCorp Serf](https://www.serf.io/) for decentralized cluster membership, failure detection, and event dissemination.

## ✨ Features

-  **Full Serf Protocol** - Complete implementation of Serf 1.6.x
-  **SWIM Gossip** - Scalable, weakly consistent infection-style dissemination
-  **Service Discovery** - Distributed service registration and discovery
-  **Lighthouse** - Join the cluster without hardcoding a node address
-  **ASP.NET Core Integration** - First-class dependency injection support
-  **RPC Client/Server** - Complete RPC implementation with authentication
-  **Event Handlers** - Custom event processing and queries
-  **Network Coordinates** - Vivaldi-based network latency estimation
-  **Encryption Support** - AES-256 GCM gossip encryption
-  **Snapshot Recovery** - Persistent cluster state across restarts

## 🚀 Quick Start

### Installation

```bash
dotnet add package NSerf
```

### Basic Usage with ASP.NET Core

```csharp
using NSerf.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Serf with default configuration
builder.Services.AddNSerf(options =>
{
    options.NodeName = "web-server-1";
    options.BindAddr = "0.0.0.0:7946";
    options.Tags["role"] = "web";
    options.StartJoin = new[] { "10.0.1.10:7946" };
});

var app = builder.Build();
app.Run();
```

### Using Serf Services

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

    public int GetClusterSize() => _serf.Members().Length;

    public async Task BroadcastEventAsync(string eventName, byte[] payload)
    {
        await _serf.UserEventAsync(eventName, payload, coalesce: true);
    }
}
```

### Event Handling

```csharp
using NSerf.Agent;
using NSerf.Serf.Events;

public class CustomEventHandler : IEventHandler
{
    public void HandleEvent(Event evt)
    {
        switch (evt)
        {
            case MemberEvent memberEvent:
                // Handle member join/leave/failed events
                break;
            case UserEvent userEvent:
                // Handle custom user events
                break;
            case Query query:
                // Respond to queries
                await query.RespondAsync(responseData);
                break;
        }
    }
}
```

## 📦 What's Included

- **NSerf.Agent** - SerfAgent for managing cluster lifecycle
- **NSerf.Client** - RPC client for remote management
- **NSerf.Extensions** - ASP.NET Core dependency injection
- **NSerf.Memberlist** - SWIM-based gossip protocol
- **NSerf.Serf** - Core cluster coordination

## 🔧 Configuration Options

```csharp
builder.Services.AddNSerf(options =>
{
    options.NodeName = "web-server-1";          // Unique node name
    options.BindAddr = "0.0.0.0:7946";          // Bind address
    options.EncryptKey = "base64-key";          // Optional encryption
    options.Tags["role"] = "web";               // Node metadata
    options.StartJoin = ["node1:7946"]; // Bootstrap nodes
    options.RetryJoin = ["node1:7946"]; // Auto-rejoin
    options.SnapshotPath = "/var/serf/snap";    // State persistence
    options.RejoinAfterLeave = true;            // Allow rejoin
});
```

## 📚 Documentation

- **GitHub Repository**: https://github.com/BoolHak/NSerfProject
- **Full Documentation**: See README.md in repository
- **Example Applications**: Distributed chat with Docker support
- **API Reference**: XML documentation included

## 🎯 Use Cases

- **Service Discovery** - Automatic node discovery and health monitoring
- **Cluster Coordination** - Distributed consensus and leader election
- **Event Broadcasting** - Reliable event dissemination across nodes
- **Failure Detection** - Automatic detection of failed nodes
- **Configuration Management** - Dynamic cluster reconfiguration
- **Load Balancing** - Distributed request routing

## 🧪 Battle-Tested

- 1260+ comprehensive tests
-  Full Serf 1.6.x protocol compatibility
-  Production-ready encryption
-  Cross-platform support (Windows, Linux, macOS)

## 📝 License

MPL-2.0 - Same license as HashiCorp Serf

## 🤝 Contributing

Contributions welcome! Visit our GitHub repository for issues and PRs.

## 🔗 Resources

- [HashiCorp Serf Docs](https://www.serf.io/)
- [SWIM Paper](https://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf)
- [GitHub Repository](https://github.com/BoolHak/NSerfProject)
