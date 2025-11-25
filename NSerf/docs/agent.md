# Agent

## Purpose

The agent layer (`NSerf.Agent.*`) runs Serf as a long-lived process with configuration loading, event handling, RPC exposure, and integrations such as Lighthouse and mDNS.

## AgentConfig

`AgentConfig` is the high-level configuration model for the agent. It is used by:

- `ConfigLoader` for JSON-based configuration.
- `AgentCommand` for CLI flags.
- `SerfAgent` to build a concrete Serf `Config` and `MemberlistConfig`.

Important groups of properties:

- Identity and networking:
  - `NodeName`, `Role`, `BindAddr`, `AdvertiseAddr`.
- RPC:
  - `RpcAddr`, `RpcAuthKey`.
- Tags and snapshot:
  - `Tags`, `TagsFile`, `SnapshotPath`.
- Membership behavior:
  - `Protocol`, `RejoinAfterLeave`, `ReplayOnJoin`.
  - `StartJoin`, `RetryJoin`, WAN variants and intervals.
- Features and flags:
  - `DisableCoordinates`, `DisableNameResolution`, `EnableCompression`, `LeaveOnTerm`, `SkipLeaveOnInt`.
- Discovery and Lighthouse:
  - `Mdns`, `UseLighthouseStartJoin`, `UseLighthouseRetryJoin`, `LighthouseVersionName`, `LighthouseVersionNumber`, `LighthouseBaseUrl`, etc.
- Event handling:
  - `EventHandlers : List<string>` – event handler specifications.

`AgentConfig.Merge` defines how multiple configs (defaults, file, CLI) are combined.

## SerfAgent

`SerfAgent` wraps a `Serf` instance and orchestrates:

- Construction of Serf `Config` from `AgentConfig`.
- Creation of `MemberlistConfig`, including wiring of `EnableCompression`.
- Startup and shutdown of the Serf node.
- Event loop that consumes `Config.EventCh` and dispatches to handlers.
- Startup of the RPC server.

### Lifecycle

Typical usage:

```csharp
var agentConfig = new AgentConfig { NodeName = "node1", BindAddr = "127.0.0.1:7946" };
await using var agent = new SerfAgent(agentConfig);
await agent.StartAsync();
// ... use RPC or local APIs ...
await agent.ShutdownAsync();
```

Lifecycle safeguards include:

- Protection against starting twice.
- Idempotent shutdown.
- Automatic agent shutdown when Serf shuts down unexpectedly (event channel closure detection).

## Event Handlers and Scripts

The agent exposes a pluggable event handler system.

- `IEventHandler`
  - Interface implemented by components that want to receive Serf events.

- `ScriptEventHandler`
  - Wraps a set of `EventScript` descriptors.
  - Filters events by type and (optionally) name using `EventFilter`.
  - Invokes scripts asynchronously via `ScriptInvoker`.

- `ScriptInvoker`
  - Executes scripts through the platform shell (`cmd /C` on Windows, `/bin/sh -c` on Unix).
  - Creates environment variables such as `SERF_EVENT`, `SERF_SELF_NAME`, `SERF_TAG_*`, `SERF_USER_EVENT`, `SERF_USER_LTIME`.
  - Supports stdin for member events, user events, and queries.

### Configuring Event Handlers

Event handlers can be supplied via:

- `AgentConfig.EventHandlers` from JSON config (field `event_handlers`).
- CLI `--event-handler` option in `AgentCommand`.

Examples of handler specs:

- `"member-join=handler.sh"` – script for member join events.
- `"member-leave,member-failed=handler.sh"` – multiple member events.
- `"user:deploy=deploy-handler.sh"` – user event `deploy`.
- `"handler.sh"` – wildcard for all events.

## ConfigLoader

`ConfigLoader` is responsible for reading configuration from disk:

- `LoadFromFileAsync(path)` – load a single JSON file into `AgentConfig`.
- `LoadFromDirectoryAsync(path)` – merge all `*.json` files lexicographically into a single `AgentConfig`.
- `Validate(AgentConfig)` – applies defaults (node name, bind address, RPC address, log level).

The JSON serializer uses `snake_case` field names, so `AgentConfig.EventHandlers` maps to `event_handlers` in JSON.
