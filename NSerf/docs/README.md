# NSerf Project Documentation

## Overview

NSerf is a C#/.NET implementation of HashiCorp Serf: a decentralized cluster membership, failure detection, and orchestration system.

It provides:
- **Gossip-based membership** and failure detection.
- **User events** and **queries** for cluster-wide notifications and RPC-style interactions.
- A long-running **Serf agent** with a rich **CLI** and **scriptable event handlers**.
- Integration points for **.NET dependency injection** and examples for HTTP/YARP/ToDo/Chat scenarios.

Targets: .NET 8.

---

## High-Level Architecture

NSerf mirrors the Go Serf design but in idiomatic C# with explicit concurrency primitives.

### Core Layers

- **Memberlist** (`NSerf.Memberlist.*`)
  - Gossip layer: manages nodes, heartbeats, failure detection.
  - Configured via `MemberlistConfig`.
  - Used internally by Serf for cluster membership.
  - See: [Memberlist details](./memberlist.md).

- **Serf Core** (`NSerf.Serf.*`)
  - `Serf` class is the orchestrator.
  - Manages:
    - Membership state machine (`SerfState`, `ClusterCoordinator`).
    - Event pipeline (`EventManager`, snapshotting, internal queries).
    - Coordinates, key management, conflict resolution.
  - Emits events into a configurable channel (`Config.EventCh`).
  - See: [Serf core details](./serf-core.md).

- **Agent** (`NSerf.Agent.*`)
  - Long-running process that hosts a Serf instance.
  - Responsibilities:
    - Build `Config` + `MemberlistConfig` from `AgentConfig`.
    - Start/stop Serf and RPC server.
    - Maintain event loop and dispatch events to registered handlers.
    - Manage tags, keyring, snapshot path, Lighthouse discovery, mDNS.
    - Load/merge configuration from JSON files and CLI flags.
  - See: [Agent details](./agent.md).

- **CLI** (`NSerf.CLI.*`)
  - `AgentCommand` starts the agent as a console app.
  - Uses `System.CommandLine` to expose options like `--node`, `--bind`, `--event-handler`, `--config-file`, etc.
  - Talks to the agent via RPC for commands like `members`, `join`, `leave`, `event`, `query`, `force-leave`, `tags`.
  - See: [CLI details](./cli.md).

- **Examples**
  - `NSerf.BackendService`, `NSerf.YarpExample`, `NSerf.ChatExample`, `NSerf.ToDoExample` show integration patterns (HTTP, reverse proxy, etc.).

---

## Major Types and Components

### `NSerf.Serf.Serf`

Central class representing a Serf node.

- Created via `Serf.CreateAsync(Config config)`.
- Key members:
  - `Memberlist`: underlying gossip transport.
  - `State()`: returns `SerfState` (Alive, Leaving, Left, Shutdown).
  - `IsReady()`: indicates that Serf is alive and `Memberlist` is initialized.
  - `JoinAsync`, `LeaveAsync`, `ShutdownAsync`.
  - `UserEventAsync`, `QueryAsync`, `SetTagsAsync`.

### `NSerf.Serf.Config`

- Serf runtime configuration.
- Important fields:
  - `NodeName`, `Tags`, `ProtocolVersion`.
  - `SnapshotPath` for state persistence.
  - `RejoinAfterLeave`, `DisableCoordinates`.
  - `EventCh`: channel writer for events (hook used by the agent).
  - `MemberlistConfig`: nested config for memberlist.

### `NSerf.Agent.AgentConfig`

- High-level agent configuration model, including:
  - Basic: `NodeName`, `BindAddr`, `AdvertiseAddr`, `EncryptKey`, `RpcAddr`, `RpcAuthKey`, `Tags`, `TagsFile`.
  - Rejoin / retry join: `RejoinAfterLeave`, `ReplayOnJoin`, `StartJoin`, `RetryJoin`, WAN variants.
  - Features: `DisableCoordinates`, `DisableNameResolution`, `EnableCompression`, `LeaveOnTerm`, `SkipLeaveOnInt`.
  - Lighthouse, mDNS, metrics, limits.
  - `EventHandlers`: a list of event handler specifications (`"member-join=handler.sh"`, `"user:deploy=deploy.sh"`, `"handler.sh"` for wildcard).

### `NSerf.Agent.SerfAgent`

- Wraps a `Serf` instance.
- Responsibilities:
  - Build Serf `Config` from `AgentConfig` (including `MemberlistConfig` and compression flag mapping).
  - Load tags, keyring, and config from JSON files (via `ConfigLoader`).
  - Start / stop RPC server.
  - Maintain an event loop reading from `Config.EventCh` and dispatching to registered `IEventHandler`s.
  - Manage script-based event handlers through `ScriptEventHandler` and `ScriptInvoker`.

### Event Handling and Scripts

- `EventManager` (Serf side) emits `IEvent` instances into the configured channel.
  - Member events: `MemberEvent` with `EventType.MemberJoin` / `Leave` / `Failed` / `Update` / `Reap`.
  - User events: `UserEvent`.
  - Queries: `Query`.

- `SerfAgent` subscribes to this channel and dispatches events:
  - `ScriptEventHandler` filters events (`EventFilter`) and invokes scripts via `ScriptInvoker`.
  - `ScriptInvoker` runs scripts through the platform shell and wires environment variables like:
    - `SERF_EVENT` (e.g. `member-join`, `user`, `query`).
    - `SERF_SELF_NAME`, `SERF_SELF_ROLE`, `SERF_TAG_*`.
    - `SERF_USER_EVENT`, `SERF_USER_LTIME`.

---

## Quickstart: Running the Agent

### Prerequisites

- .NET 8 SDK installed.
- Repository cloned locally.

### Build

```bash
# From NSerf root
dotnet build
```

### Start a Local Agent

From the `NSerf` directory:

```bash
dotnet run --project NSerf.CLI -- agent --node node1 --bind 127.0.0.1:7946 --rpc-addr 127.0.0.1:7373
```

This starts an agent with:
- Node name `node1`.
- Gossip bind address `127.0.0.1:7946`.
- RPC server listening on `127.0.0.1:7373`.

### Start a Second Agent and Join

In another shell:

```bash
dotnet run --project NSerf.CLI -- agent --node node2 --bind 127.0.0.1:7947 --rpc-addr 127.0.0.1:7374 --join 127.0.0.1:7946
```

Now `node2` will join `node1`.

### List Members

In a third shell, query RPC on `node1`:

```bash
dotnet run --project NSerf.CLI -- members --rpc-addr 127.0.0.1:7373
```

You should see both `node1` and `node2` with status `alive`.

---

## Quickstart: Script Event Handlers

You can attach scripts that run on specific Serf events.

### Example: Script on Member Join

Create a simple script:

**Windows (`handler.bat`)**
```bat
@echo off
echo Member joined: %SERF_SELF_NAME% >> join.log
```

**Unix (`handler.sh`)**
```bash
#!/bin/bash
echo "Member joined: $SERF_SELF_NAME" >> join.log
```

Make it executable on Unix:

```bash
chmod +x handler.sh
```

Start the agent with event handler:

```bash
# Windows
dotnet run --project NSerf.CLI -- agent --node node1 --bind 127.0.0.1:7946 ^
  --rpc-addr 127.0.0.1:7373 ^
  --event-handler "member-join=handler.bat"

# Unix
dotnet run --project NSerf.CLI -- agent --node node1 --bind 127.0.0.1:7946 \
  --rpc-addr 127.0.0.1:7373 \
  --event-handler "member-join=./handler.sh"
```

When another node joins this cluster, the script will be invoked and `join.log` will be updated.

---

## Configuration Files

The agent can load configuration from JSON files (or directories of JSON files) using `AgentConfig` + `ConfigLoader`.

Minimal example (`config.json`):

```json
{
  "node_name": "config-node",
  "bind_addr": "0.0.0.0:7946",
  "rpc_addr": "127.0.0.1:7373",
  "event_handlers": [
    "member-join=./handler.sh",
    "user:deploy=./deploy-handler.sh"
  ]
}
```

Run agent with config:

```bash
dotnet run --project NSerf.CLI -- agent --config-file ./config.json
```

CLI flags override values from config files (merge semantics are implemented in `AgentConfig.Merge`).

---

## Testing

The project has an extensive test suite under `NSerfTests` and `NSerf.CLI.Tests` covering:

- Serf core state machine and membership behaviors.
- Event manager, snapshotting, and queries.
- Agent lifecycle, configuration, tags, keyring.
- RPC server/client, security, filtering.
- Script invocation and environment variables.

To run all tests:

```bash
dotnet test
```

To run a specific test class (example):

```bash
dotnet test --filter "FullyQualifiedName~AgentScriptInvocationTests"
```

---

## Next Steps for Documentation

Planned extensions for this documentation set:

- Detailed state machine diagrams and membership semantics.
- RPC protocol and message formats.
- Deep dive into configuration (`AgentConfig` and `Config`).
- Guides for integrating NSerf into ASP.NET Core apps and workers.
- Troubleshooting and operational best practices.
