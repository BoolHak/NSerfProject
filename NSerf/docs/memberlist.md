# Memberlist Layer

## Purpose

The memberlist layer implements the gossip-based cluster membership and failure-detection protocol. It is a lower-level component that Serf builds on.

Namespaces: `NSerf.Memberlist.*`, `NSerf.Memberlist.Configuration.*`, `NSerf.Memberlist.Transport.*`, `NSerf.Memberlist.Security.*`, `NSerf.Memberlist.Delegates.*`.

## Responsibilities

- Maintain the list of nodes in the cluster.
- Periodically probe peers to detect failures.
- Disseminate state changes (joins, leaves, failures) via gossip.
- Provide TCP push/pull state syncs for full state convergence.
- Enforce optional encryption and IP-based access control.
- Expose configuration knobs for timeouts, suspicion, retransmits, compression, and encryption.

## Key Types

### `Memberlist`

Main entry point for the gossip layer:

- Owns UDP and TCP transports (`ITransport` / `NetTransport`).
- Encodes/decodes messages for probes, acks, gossip, and push/pull.
- Drives probe loops and suspicion / failure-detection logic using values from `MemberlistConfig`.

### `MemberlistConfig`

Configuration object that controls all behavior of a `Memberlist` instance.

Networking and identity:

- `Name` – node name, must be unique in the cluster.
- `BindAddr`, `BindPort` – local bind address/port for UDP and TCP gossip.
- `AdvertiseAddr`, `AdvertisePort` – externally advertised address/port (for NAT).
- `Transport` – custom `ITransport`; if null, a default `NetTransport` is created from bind settings.

Protocol and timeouts:


Failure detection and suspicion:

- `ProbeInterval` – how often a random node is probed.
- `ProbeTimeout` – ack timeout; should approximate high-percentile RTT.
- `IndirectChecks` – number of indirect probes used when direct probes fail.
- `SuspicionMult` – base multiplier for suspicion time.
- `SuspicionMaxTimeoutMult` – upper bound multiplier for suspicion time.
- `AwarenessMaxMultiplier` – how much probe intervals can be stretched when the node is overloaded.

Gossip and state sync:

- `RetransmitMult` – multiplier for broadcast retransmits: `retransmits = RetransmitMult * log(N+1)`.
- `GossipInterval` – interval between non-piggyback gossip messages.
- `GossipNodes` – number of random nodes to gossip to per interval.
- `GossipToTheDeadTime` – how long to continue gossiping to dead nodes (allows refutation).
- `PushPullInterval` – interval between full state syncs over TCP; zero disables push/pull.

Transport behavior:

- `DisableTcpPings` – completely disables TCP fallback when UDP probes fail.
- `DisableTcpPingsForNode` – optional callback to disable TCP pings selectively.
- `HandoffQueueDepth` – depth of the internal UDP processing queue.
- `UDPBufferSize` – maximum size of UDP packets.

Security and encryption:

- `SecretKey` – initial symmetric key material for gossip encryption (16/24/32 bytes).
- `Keyring` – holds all encryption keys; if populated, `EncryptionEnabled()` is true.
- `GossipVerifyIncoming` / `GossipVerifyOutgoing` – enforce encrypted gossip on receive/send.
- `Label` – optional label included on packets and streams; used as authenticated data when encryption is enabled.
- `SkipInboundLabelCheck` – if true, skips label verification on inbound data.

IP access control:

- `CIDRsAllowed : List<IPNetwork>` – allowed CIDR ranges; empty means block all when checked, null-like (no items) is treated as allow-any.
- `IPMustBeChecked()` – returns true when access control is active.
- `IPAllowed(IPAddress ip)` – returns `null` if allowed, or an error string if blocked.

Delegates and callbacks:

- `Delegate : IDelegate?` – provides user data and receives certain callbacks.
- `Events : IEventDelegate?` – notified of member join/leave/failure events at the memberlist level.
- `Conflict : IConflictDelegate?` – handles name conflicts.
- `Merge : IMergeDelegate?` – manages cluster merge operations.
- `Ping : IPingDelegate?` – measures RTT and can add payloads to acks.
- `Alive : IAliveDelegate?` – can veto or modify nodes during join.
- `DelegateProtocolVersion`, `DelegateProtocolMin`, `DelegateProtocolMax` – control delegate protocol negotiation.

Misc:

- `DNSConfigPath` – path to system DNS config (`/etc/resolv.conf` by default).
- `QueueCheckInterval` – interval to check queue depth/health.
- `MsgpackUseNewTimeFormat` – toggles msgpack time encoding format.
- `Logger` – `ILogger` for diagnostics.

### Default Configs

Memberlist exposes helpers for sane defaults:

- `DefaultLANConfig()` – tuned for typical LAN conditions.
- `DefaultWANConfig()` – derived from LAN but with larger timeouts and intervals suitable for WAN.
- `DefaultLocalConfig()` – optimized for local/loopback testing (shorter intervals and timeouts).

These methods set consistent values for all timing-related fields, gossip parameters, and security defaults.

## How Serf Uses Memberlist

- Serf constructs a `MemberlistConfig` instance when building its `Config`.
- In the agent layer, `SerfAgent.BuildConfig()` starts from `MemberlistConfig.DefaultLANConfig()` and then applies overrides from `AgentConfig`:
  - Bind address/IP and port.
  - Advertise address and port (when specified).
  - Encryption and keyring (via `EncryptKey` or keyring file loading).
  - Compression via `EnableCompression`.
- `Serf.CreateAsync` receives the fully-populated `Config`, creates `Memberlist`, and wires delegates and event callbacks.
- Memberlist callbacks are used to translate low-level membership changes into high-level Serf `MemberEvent`s (join, leave, failed, update, reap).

Conceptually:

1. Memberlist detects peers and gossip changes.
2. Memberlist reports events via `IEventDelegate` / other delegates.
3. Serf converts these into Serf events and feeds them into the event pipeline (snapshotter, internal queries, `Config.EventCh`).

## Configuration Flow

1. **AgentConfig** – high-level configuration from JSON and CLI:
   - `BindAddr`, `AdvertiseAddr`, encryption settings, compression flag, retry/join behavior.
2. **SerfAgent.BuildConfig()** – converts `AgentConfig` into Serf `Config`:
   - Creates `MemberlistConfig` using `MemberlistConfig.DefaultLANConfig()` as a baseline.
   - Overrides bind/advertise, encryption, and `EnableCompression` from `AgentConfig`.
3. **Serf.CreateAsync(Config)** – uses `Config.MemberlistConfig` to create and initialize `Memberlist`.
4. **Runtime** – memberlist runs its probe/gossip loops and notifies Serf of membership changes.

For higher-level behavior such as join/leave semantics and compression usage, see the Serf core and Agent documentation.
