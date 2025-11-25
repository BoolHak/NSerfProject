# Serf Events and Pipeline

## Event Types

Serf represents events via the `NSerf.Serf.Events` namespace.

Main event types:

- `MemberEvent` – membership changes.
  - `Type : EventType` – `MemberJoin`, `MemberLeave`, `MemberFailed`, `MemberUpdate`, `MemberReap`.
  - `Members : Member[]` – affected members.
- `UserEvent` – user-defined broadcast events.
  - `Name`, `Payload`, `LTime`.
- `Query` – request/response style events.
  - `Name`, `Payload`, `LTime`, response channel.

## EmitEvent Helper

Inside `Serf`, all events flow through a private helper, conceptually:

- Writes the event to an internal IPC channel (`IpcEventReader`) so RPC clients can stream events.
- Forwards the event into the `EventManager` pipeline.

## EventManager and Pipeline

The event pipeline is constructed roughly as:

1. **Base destination** – `Config.EventCh` (if not null).
2. **Snapshotter (optional)** – if `SnapshotPath` is configured:
   - Snapshotter is inserted between Serf and the final destination.
   - It persists events to disk and forwards them.
3. **Query handler** – wraps the current destination to support internal queries.
4. **EventManager** – is created with the final destination channel.

This yields the sequence:

- Serf → EventManager → [Snapshotter] → [QueryHandler] → `Config.EventCh`.

## Agent Integration

When running under the agent:

- `Config.EventCh` is set to a bounded channel owned by `SerfAgent`.
- `SerfAgent` runs an event loop that reads from this channel and dispatches events to `IEventHandler` implementations.
- `ScriptEventHandler` is one such handler that forwards events to external scripts via `ScriptInvoker`.

## Queries

Queries are special events that expect responses:

- A `Query` event includes a response channel.
- Scripts may respond to queries via `ScriptInvoker` by writing to stdout; the result is captured and sent back as a response.
- Serf aggregates responses and exposes them via query APIs.

## Snapshotting

If `SnapshotPath` is specified:

- A snapshotter is created:
  - It consumes events from the pipeline.
  - Writes them to a snapshot file.
  - Forwards them to the next stage.
- This makes it possible to rebuild state after restarts by replaying snapshots.

For configuration details related to events and snapshotting, see `serf-config.md`.
