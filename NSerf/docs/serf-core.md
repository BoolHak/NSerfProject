# Serf Core

## Purpose

The Serf core (`NSerf.Serf.*`) is the heart of the system. It orchestrates membership, events, queries, snapshotting, and internal coordination on top of the memberlist layer.

This file provides a high-level overview and links to more detailed documents.

---

## Main Entry Point: `Serf`

Namespace: `NSerf.Serf`

Key responsibilities:

- Manage the lifecycle of a Serf node.
- Maintain node state via `SerfState` and `ClusterCoordinator`.
- Provide public APIs for joining, leaving, shutting down, and querying the cluster.
- Route events into the event pipeline and optional snapshotter.

Important methods include:

- `CreateAsync(Config config)` – create and initialize a Serf instance.
- `State()` – report current lifecycle state.
- `IsReady()` – report readiness (alive + memberlist initialized).
- `JoinAsync`, `LeaveAsync`, `ShutdownAsync` – membership and lifecycle.
- `UserEventAsync`, `QueryAsync`, `SetTagsAsync` – interaction and metadata.

For deeper details, see the sections below.

---

## Detailed Topics

- **Configuration** – how `Config` and `MemberlistConfig` control Serf behavior, snapshots, and event destinations.
  - See: [Serf configuration](./serf-config.md)

- **State and coordination** – lifecycle states, `SerfState`, `ClusterCoordinator`, and readiness semantics.
  - See: [Serf state and coordination](./serf-state.md)

- **Events and pipeline** – `MemberEvent`, `UserEvent`, `Query`, `EventManager`, snapshotter, and how events reach the agent.
  - See: [Serf events and pipeline](./serf-events.md)

These documents together describe the full behavior of the Serf core and how it interacts with memberlist and the agent.
