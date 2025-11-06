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
<PackageReference Include="NSerf" Version="0.1.3-beta" />
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

The `NSerf.ChatExample` project demonstrates real-world NSerf usage with a distributed chat application featuring SignalR for real-time communication and Serf for cluster coordination.

### Features

- **Cluster Membership**: Nodes automatically discover each other
- **Distributed Messaging**: Messages broadcast across all nodes via Serf user events
- **Automatic Failover**: Nodes continue operating if others go down
- **Real-time UI**: SignalR provides instant updates
- **Docker Support**: Run a 3-node cluster with one command

### Architecture

```
┌─────────────────┐         ┌─────────────────┐         ┌─────────────────┐
│   Browser 1     │         │   Browser 2     │         │   Browser 3     │
│  (WebSocket)    │         │  (WebSocket)    │         │  (WebSocket)    │
└────────┬────────┘         └────────┬────────┘         └────────┬────────┘
         │ SignalR                   │ SignalR                   │ SignalR
┌────────▼────────┐         ┌────────▼────────┐         ┌────────▼────────┐
│   Chat Node 1   │◄───────►│   Chat Node 2   │◄───────►│   Chat Node 3   │
│   (ASP.NET)     │  Serf   │   (ASP.NET)     │  Serf   │   (ASP.NET)     │
└─────────────────┘         └─────────────────┘         └─────────────────┘
         └───────────────────────────┴───────────────────────────┘
                       Serf Cluster Membership
```

### Quick Start - Docker

Run a 3-node cluster:

```bash
cd NSerf/NSerf.ChatExample
docker-compose up --build
```

Access the chat:
- **Node 1**: http://localhost:5000
- **Node 2**: http://localhost:5001
- **Node 3**: http://localhost:5002

Stop the cluster:

```bash
docker-compose down
```

### Quick Start - Manual

**Terminal 1 - Bootstrap Node:**
```bash
cd NSerf.ChatExample
dotnet run
```

**Terminal 2 - Node 2:**
```bash
dotnet run chat-node-2 5001 7947 "127.0.0.1:7946"
```

**Terminal 3 - Node 3:**
```bash
dotnet run chat-node-3 5002 7948 "127.0.0.1:7946"
```

Then open http://localhost:5000, http://localhost:5001, and http://localhost:5002 in different browsers!

### API Endpoints

Each node exposes:

- **`GET /`** - Chat UI
- **`GET /health`** - Health check
- **`GET /members`** - Cluster membership (JSON)
- **`/chatHub`** - SignalR WebSocket endpoint

### Testing Features

**Test 1: Message Broadcasting**
1. Open 3 browser tabs (one per node)
2. Send a message from any tab
3. Verify it appears in all tabs

**Test 2: Node Failure**
```bash
# Stop node 2
docker-compose stop chat-node-2

# Messages still work between nodes 1 and 3

# Restart node 2
docker-compose start chat-node-2

# Node 2 rejoins automatically
```

**Test 3: Cluster Status**
```bash
curl http://localhost:5000/members | jq
```

---

## YARP Service Discovery Example

The `NSerf.YarpExample` project demonstrates **production-ready service discovery and load balancing** by integrating NSerf with Microsoft YARP (Yet Another Reverse Proxy). This showcases the **real production value** of NSerf for microservices architectures.

### Why This Matters

This example solves a critical production problem: **How do you dynamically discover and load balance backend services without external dependencies like Consul or Eureka?**

**NSerf + YARP provides:**
- ✅ **Zero-configuration service discovery** - No hardcoded endpoints
- ✅ **Automatic load balancing** - Round-robin across healthy nodes
- ✅ **Dynamic scaling** - Add/remove services without restarting the proxy
- ✅ **Health monitoring** - Automatic removal of failed backends
- ✅ **Encrypted cluster communication** - AES-256-GCM secure gossip
- ✅ **Persistent snapshots** - Cluster state survives restarts
- ✅ **No external dependencies** - Everything built into your .NET application

### Architecture

```
Client Request → YARP Proxy → [NSerf Cluster Discovery] → Backend-1
                                                         → Backend-2
                                                         → Backend-3
```

The YARP proxy:
1. Joins the Serf cluster with role `proxy`
2. Discovers backend services tagged with `service=backend`
3. Dynamically updates YARP routing configuration
4. Load balances requests across healthy backends
5. Monitors backend health and removes failed nodes

### Quick Start - Docker

Run a complete setup with 1 proxy and 3 backend services:

```bash
cd NSerf/NSerf.YarpExample
docker-compose up --build
```

This starts:
- **YARP Proxy** on `http://localhost:8080`
- **Backend-1** on `http://localhost:5001`
- **Backend-2** on `http://localhost:5002`
- **Backend-3** on `http://localhost:5003`

### Testing Service Discovery

**1. Verify the proxy discovered all backends:**
```bash
curl http://localhost:8080/proxy/members | jq
```

**2. Test load balancing (requests round-robin across backends):**
```bash
for i in {1..10}; do
  curl http://localhost:8080/api/info | jq '.instance'
done
```

Output shows requests distributed: `backend-1`, `backend-2`, `backend-3`, `backend-1`, ...

**3. Test dynamic service addition:**
```bash
# Add a new backend - it's automatically discovered and added to load balancing!
docker-compose up -d --scale backend-3=2

# Wait 5 seconds for discovery, then:
curl http://localhost:8080/proxy/members | jq '.backends'
# Shows 4 backends now!
```

**4. Test automatic failover:**
```bash
# Stop a backend
docker-compose stop backend-2

# Requests automatically route around the failed node
for i in {1..10}; do
  curl http://localhost:8080/api/info | jq '.instance'
done
# Only shows backend-1 and backend-3

# Restart it - automatically rejoins
docker-compose start backend-2
```

### API Endpoints

**YARP Proxy (port 8080):**
- **`/{any-path}`** - Proxied to discovered backends
- **`GET /proxy/health`** - Proxy health check
- **`GET /proxy/members`** - View discovered services

**Backend Services (ports 5001-5003):**
- **`GET /health`** - Health check endpoint
- **`GET /api/info`** - Backend instance information
- **`GET /api/work/{id}`** - Simulates processing work
- **`GET /api/cluster`** - View cluster from backend perspective

### Production Use Cases

**1. Microservices API Gateway**
- Services auto-register on startup
- Gateway discovers and routes automatically
- No manual configuration management

**2. Multi-Region Load Balancing**
```csharp
options.Tags["service"] = "api";
options.Tags["region"] = "us-east-1";
options.Tags["zone"] = "1a";
```

**3. Canary Deployments**
```csharp
options.Tags["version"] = "2.0";
options.Tags["canary"] = "true";
// Route 90% to stable, 10% to canary
```

**4. Service Mesh**
- Combined with mTLS
- Circuit breakers
- Advanced health checks
- Traffic shaping

### Why This Is Important

This example demonstrates that **NSerf provides enterprise-grade service discovery** for .NET microservices:

- **No Consul/Eureka needed** - Gossip protocol handles discovery
- **No Kubernetes required** - Works anywhere .NET runs
- **Production-ready** - Used patterns from HashiCorp and Netflix
- **Simple integration** - < 200 lines of integration code
- **Zero external dependencies** - Everything in your .NET stack

For detailed documentation, see [`NSerf.YarpExample/README.md`](NSerf/NSerf.YarpExample/README.md).

---

## Docker Deployment

### Port Mappings

| Service | Container Port | Host Port | Purpose |
|---------|---------------|-----------|---------|
| chat-node-1 | 5000 | 5000 | HTTP/WebSocket |
| chat-node-1 | 7946 | 7946 | Serf Gossip |
| chat-node-2 | 5000 | 5001 | HTTP/WebSocket |
| chat-node-2 | 7946 | 7947 | Serf Gossip |
| chat-node-3 | 5000 | 5002 | HTTP/WebSocket |
| chat-node-3 | 7946 | 7948 | Serf Gossip |

### Monitoring

**View Logs:**
```bash
docker-compose logs -f
docker-compose logs -f chat-node-1
docker-compose logs --tail=50 -f
```

**Check Health:**
```bash
curl http://localhost:5000/health
curl http://localhost:5001/members | jq
```

### Troubleshooting

**Containers Won't Start:**
```bash
docker-compose build --no-cache
docker-compose up --build --force-recreate
```

**Nodes Can't Join:**
```bash
docker network inspect nserfchatexample_serf-chat-network
docker exec chat-node-2 ping chat-node-1
```

**Clear Everything:**
```bash
docker-compose down -v --rmi all
```

### Production Deployment

For production:
1. Use Kubernetes or Docker Swarm
2. Enable encryption (`options.EncryptKey`)
3. Add persistent volumes for snapshots
4. Configure health checks and monitoring
5. Use reverse proxy (Nginx/Traefik)
6. Enable HTTPS with proper certificates
7. Set resource limits

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
- ✅ Core Serf protocol implementation
- ✅ SWIM-based memberlist gossip
- ✅ RPC client/server with authentication
- ✅ CLI tool (drop-in replacement)
- ✅ ASP.NET Core integration
- ✅ Event handlers and queries
- ✅ Docker deployment examples
- ✅ 1230+ comprehensive tests

---

## Licensing

All source files retain the original MPL-2.0 license notices from HashiCorp Serf. The port is distributed under the same MPL-2.0 terms.
