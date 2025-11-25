# Serf State and Coordination

## SerfState

`SerfState` is an enum representing the lifecycle of a Serf node. It is used by the core and by `ClusterCoordinator` to control transitions.

Typical values:

- `SerfAlive` – node is alive and participating in the cluster.
- `SerfLeaving` – node has initiated a graceful leave.
- `SerfLeft` – node has completed a graceful leave.
- `SerfShutdown` – node has been shut down.

## ClusterCoordinator

`ClusterCoordinator` encapsulates the state machine for a single Serf instance.

Responsibilities:

- Track the current `SerfState`.
- Guard transitions using internal synchronization so only one transition runs at a time.
- Provide methods for transitioning into leaving, left, and shutdown states.

Typical transitions:

- `None → SerfAlive` – when Serf is created and fully started.
- `SerfAlive → SerfLeaving` – when a graceful leave is initiated.
- `SerfLeaving → SerfLeft` – when leave completes.
- `Serf* → SerfShutdown` – when shutdown is requested.

The Serf public API forwards operations like `LeaveAsync` and `ShutdownAsync` to `ClusterCoordinator` to enforce correct sequencing.

## IsReady

The Serf class exposes an `IsReady()` helper that indicates whether the node is ready for use.

Implementation is based on two conditions:

- Current state from `ClusterCoordinator` is `SerfAlive`.
- The underlying `Memberlist` instance is non-null (gossip layer initialized).

Practically, this means `IsReady()` returns `true` once the node has fully joined the cluster and memberlist has been initialized.

## Concurrency Guarantees

- State transitions are serialized inside `ClusterCoordinator` using locks/semaphores.
- Public lifecycle methods (`JoinAsync`, `LeaveAsync`, `ShutdownAsync`) rely on these guards to avoid racing transitions.
- `IsReady()` is safe to call concurrently; it reads current state and memberlist reference without mutating state.
