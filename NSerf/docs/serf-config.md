# Serf Configuration

## Config

The `NSerf.Serf.Config` type controls how a Serf node behaves at runtime.

Key properties (non-exhaustive):

- `NodeName` – logical node name.
- `Tags : Dictionary<string,string>` – node metadata.
- `MemberlistConfig : MemberlistConfig` – configuration for the underlying memberlist.
- `EventCh : ChannelWriter<IEvent>?` – event channel for external consumers (typically the agent).
- `EventBuffer` – size of the deduplication buffer for events.
- `SnapshotPath` – path where snapshots of the event stream/state are written.
- `MinSnapshotSize`, `MinSnapshotInterval` – snapshotting thresholds.
- `ProtocolVersion` – Serf protocol version.
- `RejoinAfterLeave` – whether nodes should rejoin after a graceful leave.
- `DisableCoordinates` – disable network coordinate calculation.

### MemberlistConfig

See `memberlist.md` for full details of `MemberlistConfig`. It is injected into Serf and used to create/manage the gossip layer.

### EventCh and Event Buffer

- `EventCh` is the primary hook for consuming Serf events from the outside.
- If `EventCh` is null, events are processed internally but not forwarded.
- `EventBuffer` controls the size of the buffer for deduplication and replay in the event manager layer.

### Snapshotting

When `SnapshotPath` is set:

- Serf uses a snapshotter that:
  - Receives events from the event pipeline.
  - Writes events to an on-disk snapshot file.
  - Forwards events to the next step in the pipeline.
- This allows state to be reconstructed on restart.

### Configuration Sources

In practice, `Config` is rarely created directly by consumers:

- The agent layer builds `Config` from an `AgentConfig` instance.
- The CLI (`agent` command) builds `AgentConfig` from defaults, config files, and CLI flags.

For more on this flow, see `agent.md` and `cli.md`.
