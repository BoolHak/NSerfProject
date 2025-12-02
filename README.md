# NSerf

NSerf is a full, from-scratch port of [HashiCorp Serf](https://www.serf.io/) to modern C#. The project mirrors Serf's decentralized cluster membership, failure detection, and event dissemination model while embracing idiomatic .NET patterns for concurrency, async I/O, and tooling. The codebase targets .NET 8+ and is currently in **beta** while the team polishes APIs and round-trips real-world workloads.

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/BoolHak/NSerfProject)

## Table of Contents

- [Key Differences](#key-differences-from-the-go-implementation)
- [Repository Layout](#repository-layout)
- [Getting Started](#getting-started)
- [ASP.NET Core Integration](#aspnet-core-integration)
- [Distributed Chat Example](#distributed-chat-example)
- [YARP Service Discovery Example](#yarp-service-discovery-example)
- [Docker Deployment](#docker-deployment)
- [Testing](#testing)
- [Project Status](#project-status)
- [License](#licensing)

## Key Differences from the Go Implementation

While the behaviour and surface area match Serf's reference implementation, a few platform-specific choices differ:

- **Serialization** relies on the high-performance [MessagePack-CSharp](https://github.com/neuecc/MessagePack-CSharp) stack instead of Go's native MessagePack bindings, keeping message layouts identical to the original protocol.
- **Compression** uses the built-in .NET `System.IO.Compression.GZipStream` for gossip payload compression, replacing the Go LZW (Lempel-Ziv-Welch) adapter while preserving wire compatibility.
- **Async orchestration** embraces task-based patterns and the C# transaction-style locking helpers introduced during the port, matching Go's channel semantics without blocking threads.
- **Lighthouse** - Join the cluster without hardcoding a node address
- **Service Discovery** - A basic service discovery ready to be used with .net applications or other services using the CLI 

## Repository Layout

```
NSerf/
├─ NSerf.sln                   # Solution entry point
├─ NSerf/                      # Core library (agent, memberlist, Serf runtime)
│  ├─ Agent/                   # CLI agent runtime, RPC server, script handlers
│  ├─ Client/                  # RPC client, request/response contracts
│  ├─ Extensions/              # ASP.NET Core DI integration
│  ├─ Memberlist/              # Gossip, failure detection, transport stack
│  ├─ Serf/                    # Cluster state machine, event managers, helpers
│  └─ ...
├─ NSerf.CLI/                  # dotnet CLI facade mirroring `serf` command
├─ NSerf.CLI.Tests/            # End-to-end and command-level test harnesses
├─ NSerf.ChatExample/          # Distributed chat demo with SignalR
├─ NSerf.YarpExample/          # YARP reverse proxy with dynamic service discovery
├─ NSerf.BackendService/       # Sample backend service for YARP example
├─ NSerfTests/                 # Comprehensive unit, integration, and verification tests
└─ documentation *.md          # Test plans, remediation reports, design notes
```

### Highlights by Area

- **Agent** – Implements the long-running daemon (`serf agent`) including configuration loading, RPC hosting, script invocation, and signal handling.
- **Extensions** – ASP.NET Core dependency injection integration for seamless web application integration.
- **Memberlist** – Full port of HashiCorp's SWIM-based memberlist, including gossip broadcasting, indirect pinging, and encryption support.
- **Serf** – Cluster coordination, state machine transitions, Lamport clocks, and query/event processing all live here.
- **Client** – Typed RPC requests/responses and ergonomic helpers for building management tooling.
- **CLI** – A drop-in `serf` CLI replacement built on `System.CommandLine`, sharing the same RPC surface and defaults as the Go binary.
- **ChatExample** – Real-world distributed chat application demonstrating NSerf capabilities with SignalR and Docker.
- **YarpExample** – Production-ready service discovery and load balancing with Microsoft YARP reverse proxy.
- **BackendService** – Sample microservice demonstrating auto-registration and cluster participation.

## Getting Started

### Prerequisites
- .NET SDK 8.0 (or newer)
- Docker Desktop (optional, for containerized deployment)

### Installation

#### Install from NuGet

Add NSerf to your project using the .NET CLI:

```bash
dotnet add package NSerf
```

Or via Package Manager Console in Visual Studio:

```powershell
Install-Package NSerf
```

Or add directly to your `.csproj` file:

```xml
<PackageReference Include="NSerf" Version="0.1.6-beta" />
```

**Latest Version**: [![NuGet](https://img.shields.io/nuget/v/NSerf.svg)](https://www.nuget.org/packages/NSerf/)

#### Build from Source

Alternatively, clone and build from source:

```bash
git clone https://github.com/BoolHak/NSerfProject.git
cd NSerfProject
dotnet build NSerf.sln
```

### CLI Usage

1. **Restore and build**
   ```powershell
   dotnet restore
   dotnet build NSerf.sln
   ```

2. **Run the agent locally**
   ```powershell
   dotnet run --project NSerf.CLI --agent
   ```

3. **Invoke commands against a running agent**
   ```powershell
   dotnet run --project NSerf.CLI --members
   dotnet run --project NSerf.CLI --query "ping" --payload "hello"
   ```

---

## ASP.NET Core Integration

NSerf provides first-class ASP.NET Core integration through the `NSerf.Extensions` package, allowing seamless addition of cluster membership to web applications.

### Quick Start

Add Serf to your application with default settings:

```csharp
using NSerf.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNSerf();

var app = builder.Build();
app.Run();
```

### Custom Configuration

```csharp
builder.Services.AddNSerf(options =>
{
    options.NodeName = "web-server-1";
    options.BindAddr = "0.0.0.0:7946";
    options.Tags["role"] = "web";
    options.Tags["datacenter"] = "us-east-1";
    options.StartJoin = new[] { "10.0.1.10:7946", "10.0.1.11:7946" };
    options.SnapshotPath = "/var/serf/snapshot";
    options.RejoinAfterLeave = true;
});
```

### Configuration from appsettings.json

**appsettings.json:**
```json
{
  "Serf": {
    "NodeName": "web-server-1",
    "BindAddr": "0.0.0.0:7946",
    "Tags": {
      "role": "web",
      "datacenter": "us-east-1"
    },
    "StartJoin": ["10.0.1.10:7946", "10.0.1.11:7946"],
    "RetryJoin": ["10.0.1.10:7946", "10.0.1.11:7946"],
    "SnapshotPath": "/var/serf/snapshot",
    "RejoinAfterLeave": true
  }
}
```

**Program.cs:**
```csharp
builder.Services.AddNSerf(builder.Configuration, "Serf");
```

### Using Serf in Your Services

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

    public int GetClusterSize()
    {
        return _serf.Members().Length;
    }

    public async Task BroadcastEventAsync(string eventName, byte[] payload)
    {
        await _serf.UserEventAsync(eventName, payload, coalesce: true);
    }
}
```

### Event Handling

Register custom event handlers:

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

    public void HandleEvent(Event evt)
    {
        switch (evt)
        {
            case MemberEvent memberEvent:
                foreach (var member in memberEvent.Members)
                {
                    _logger.LogInformation("Member {Name} is now {Status}",
                        member.Name, memberEvent.Type);
                }
                break;

            case UserEvent userEvent:
                _logger.LogInformation("User event {Name}", userEvent.Name);
                break;
        }
    }
}

// Register handler
builder.Services.AddSingleton<CustomEventHandler>();

// Attach in startup
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
```

### Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `NodeName` | `string` | Machine name | Unique node identifier |
| `BindAddr` | `string` | `"0.0.0.0:7946"` | Bind address (IP:Port) |
| `AdvertiseAddr` | `string?` | `null` | Advertise address for NAT |
| `EncryptKey` | `string?` | `null` | Base64 encryption key (32 bytes) |
| `RPCAddr` | `string?` | `null` | RPC server address |
| `Tags` | `Dictionary<string, string>` | Empty | Node metadata tags |
| `Profile` | `string` | `"lan"` | Network profile (lan/wan/local) |
| `SnapshotPath` | `string?` | `null` | Snapshot file path |
| `RejoinAfterLeave` | `bool` | `false` | Allow rejoin after leave |
| `StartJoin` | `string[]` | Empty | Nodes to join on startup |
| `RetryJoin` | `string[]` | Empty | Nodes to retry joining |

---

## Distributed Chat Example

For details about the `NSerf.ChatExample` project and how it uses NSerf for a distributed SignalR chat application, see:

- [`NSerf/docs/README.md`](NSerf/docs/README.md) (overview and architecture)
- [`NSerf/NSerf.ChatExample/README.md`](NSerf/NSerf.ChatExample/README.md) (example-specific usage and Docker instructions)

---

## YARP Service Discovery Example

For details about the `NSerf.YarpExample` project and how it integrates NSerf with YARP for service discovery and load balancing, see:

- [`NSerf/docs/README.md`](NSerf/docs/README.md) (overview and architecture)
- [`NSerf/NSerf.YarpExample/README.md`](NSerf/NSerf.YarpExample/README.md) (example-specific usage and Docker instructions)

---

## Docker Deployment

Docker Compose files and deployment instructions for the examples live alongside each example project. For step-by-step guidance, see:

- [`NSerf/NSerf.ChatExample/README.md`](NSerf/NSerf.ChatExample/README.md)
- [`NSerf/NSerf.YarpExample/README.md`](NSerf/NSerf.YarpExample/README.md)

These documents describe container topologies, port mappings, and troubleshooting steps.

---

## Testing

The solution ships with an extensive test suite that mirrors HashiCorp's verification matrix:

```powershell
dotnet test NSerf.sln
```

Test coverage includes:
- State machine transitions
- Gossip protocols
- RPC security
- Script execution
- CLI orchestration
- Agent lifecycle
- Event handling

---

## Project Status

NSerf is feature-complete relative to Serf 1.6.x but remains in **beta** while the team onboards additional users, tightens compatibility, and stabilizes the public API surface. Expect minor breaking changes as interoperability edge cases are addressed.

**Current Status:**
-  Core Serf protocol implementation
-  SWIM-based memberlist gossip
-  RPC client/server with authentication
-  Join the cluster without hardcoding a node address
-  CLI tool (drop-in replacement)
-  ASP.NET Core integration
-  Event handlers and queries
-  Docker deployment examples
-  1400+ comprehensive tests

---

## Licensing

All source files retain the original MPL-2.0 license notices from HashiCorp Serf. The port is distributed under the same MPL-2.0 terms.
