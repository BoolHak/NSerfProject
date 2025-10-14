# NSerf - C# Port of HashiCorp Serf

## 🎯 Project Overview

NSerf is a complete C# rewrite of HashiCorp's Serf, a decentralized solution for service discovery and orchestration. This project ports both the **Memberlist** (cluster membership library) and **Serf** (orchestration layer) from Go to C#, maintaining protocol compatibility while leveraging modern .NET features.

### Key Features to Implement
- **Cluster Membership**: SWIM-based gossip protocol for membership management
- **Failure Detection**: Adaptive failure detection with Lifeguard extensions
- **Event System**: User-defined events propagated across the cluster
- **Distributed Queries**: Fire queries to cluster nodes with filtering
- **Network Coordinates**: Vivaldi-based RTT estimation
- **Encryption**: AES-256 GCM encryption for gossip messages
- **Snapshots**: Persistent cluster state for fast recovery
- **Key Management**: Dynamic encryption key rotation

### Technology Stack
- **.NET 8+**: Target framework
- **C# 12+**: Modern language features
- **MessagePack**: Serialization (protocol compatibility)
- **System.Net.Sockets**: UDP/TCP networking
- **System.Threading.Channels**: Go channel equivalents
- **Microsoft.Extensions.Logging**: Structured logging
- **xUnit**: Testing framework

---

## 📊 Project Structure

```
NSerf/
├── NSerf/                              # Main library
│   ├── Memberlist/                     # Core membership protocol
│   │   ├── Protocol/                   # SWIM protocol implementation
│   │   ├── Transport/                  # Network layer (UDP/TCP)
│   │   ├── Security/                   # Encryption and keyring
│   │   ├── State/                      # Node state management
│   │   ├── Broadcast/                  # Gossip queue
│   │   └── Delegates/                  # Extension interfaces
│   ├── Serf/                           # Orchestration layer
│   │   ├── Core/                       # Main Serf implementation
│   │   ├── Events/                     # Event system
│   │   ├── Queries/                    # Query system
│   │   ├── Coordinate/                 # Vivaldi coordinates
│   │   ├── Snapshot/                   # Persistence
│   │   ├── KeyManagement/              # Key rotation
│   │   └── Delegates/                  # Memberlist integration
│   └── Cli/                            # Command-line interface
└── NSerfTests/                         # All tests
    ├── Memberlist/                     # Memberlist tests
    ├── Serf/                           # Serf tests
    └── Common/                         # Shared test utilities
```

---

## 🗺️ Implementation Roadmap

**Total Estimated Duration: 8-12 months**

---

## 🚀 Phase 1: Foundation & Memberlist Core
**Duration: 10-14 weeks**

### Milestone 1.1: Development Infrastructure Setup
**Duration: 2 weeks**

#### Objectives
- Set up development environment
- Validate technology choices
- Create proof-of-concepts for critical components

#### Checkpoints
- [ ] Project structure created (folders and organization)
- [ ] Core dependencies added (MessagePack, logging, etc.)
- [ ] Basic build system working
- [ ] Code style and formatting rules established
- [ ] Git workflow and branching strategy defined

#### Proof-of-Concepts
- [ ] **POC 1**: UDP async send/receive with `SocketAsyncEventArgs`
- [ ] **POC 2**: MessagePack serialization/deserialization
- [ ] **POC 3**: AES-GCM encryption/decryption with labels
- [ ] **POC 4**: Priority queue with B-tree semantics
- [ ] **POC 5**: Channel-based event propagation

#### Test Requirements
- [ ] All POCs have working unit tests
- [ ] Performance benchmarks for critical paths

---

### Milestone 1.2: Network Transport Layer
**Duration: 2 weeks**

#### Files to Port (Go → C#)
```
transport.go           → ITransport.cs
net_transport.go       → NetTransport.cs
mock_transport.go      → MockTransport.cs (in tests)
peeked_conn.go         → PeekedConnection.cs
```

#### Implementation Tasks
- [ ] Define `ITransport` interface
- [ ] Implement `NetTransport` with UDP socket
- [ ] Implement TCP stream handling
- [ ] Connection pooling for TCP
- [ ] Packet buffering and queuing
- [ ] Graceful shutdown mechanism
- [ ] Address resolution (DNS support)

#### Test Requirements
- [ ] Unit tests: Transport interface methods
- [ ] Integration tests: Loopback communication
- [ ] Tests: Packet loss simulation
- [ ] Tests: Connection timeout handling

---

### Milestone 1.3: Protocol Messages & Encoding
**Duration: 2 weeks**

#### Files to Port
```
net.go (messages)      → Messages/MessageTypes.cs
                       → Messages/ProtocolMessages.cs
util.go                → Common/NetworkUtils.cs
```

#### Implementation Tasks
- [ ] Define all message types (ping, ack, suspect, alive, dead, etc.)
- [ ] Implement MessagePack serialization for each message
- [ ] CRC32 checksum calculation
- [ ] Compound message packing/unpacking
- [ ] Message compression support (LZW)
- [ ] Label prefix handling
- [ ] Protocol version negotiation

#### Test Requirements
- [ ] Unit tests: Each message type serialization
- [ ] Tests: Round-trip serialization (encode → decode)
- [ ] Tests: Compound message assembly
- [ ] Tests: CRC validation
- [ ] Tests: Protocol version compatibility

---

### Milestone 1.4: Node State Management
**Duration: 2 weeks**

#### Files to Port
```
state.go               → State/NodeState.cs
memberlist.go          → Memberlist.cs
config.go              → Configuration/Config.cs
```

#### Implementation Tasks
- [ ] `NodeState` class with state enum
- [ ] `Memberlist` main class structure
- [ ] Node map (concurrent dictionary)
- [ ] Incarnation number management
- [ ] Sequence number management
- [ ] Node list operations
- [ ] Thread-safe state transitions
- [ ] Configuration with 30+ parameters

#### Test Requirements
- [ ] Unit tests: State transitions
- [ ] Tests: Incarnation number conflicts
- [ ] Tests: Concurrent node map access
- [ ] Tests: Configuration validation

---

### Milestone 1.5: SWIM Protocol & Failure Detection
**Duration: 3 weeks**

#### Files to Port
```
state.go (protocol)    → Protocol/SwimProtocol.cs
suspicion.go           → Protocol/SuspicionTimer.cs
awareness.go           → Protocol/HealthAwareness.cs
```

#### Implementation Tasks
- [ ] Probe scheduling and execution
- [ ] Direct ping implementation
- [ ] Indirect ping through k nodes
- [ ] Ack/Nack response handling
- [ ] Suspicion timer with acceleration
- [ ] Health awareness scoring
- [ ] Suspicion confirmation logic
- [ ] Dead node declaration

#### Test Requirements
- [ ] Unit tests: Probe lifecycle
- [ ] Tests: Suspicion timer acceleration
- [ ] Tests: Health awareness calculation
- [ ] Integration tests: Failure detection in 3-node cluster

---

### Milestone 1.6: Broadcast Queue & Gossip
**Duration: 2 weeks**

#### Files to Port
```
queue.go               → Broadcast/TransmitLimitedQueue.cs
broadcast.go           → Broadcast/IBroadcast.cs
```

#### Implementation Tasks
- [ ] `IBroadcast` interface
- [ ] `TransmitLimitedQueue` with priority ordering
- [ ] B-tree or SortedSet for priority queue
- [ ] Retransmit limit calculation
- [ ] Named broadcast support
- [ ] Message piggyback on probes

#### Test Requirements
- [ ] Unit tests: Queue priority ordering
- [ ] Tests: Retransmit limit calculation
- [ ] Performance tests: High message throughput

---

### Milestone 1.7: Security & Encryption
**Duration: 2 weeks**

#### Files to Port
```
security.go            → Security/SecurityManager.cs
keyring.go             → Security/Keyring.cs
label.go               → Security/LabelManager.cs
```

#### Implementation Tasks
- [ ] AES-256 GCM encryption
- [ ] Keyring with multiple keys
- [ ] Primary key selection
- [ ] Key installation/removal
- [ ] Label-based authentication
- [ ] Encrypt/decrypt message methods

#### Test Requirements
- [ ] Unit tests: Encrypt/decrypt round-trip
- [ ] Tests: Multiple key support
- [ ] Tests: Label validation
- [ ] Tests: Concurrent key operations

---

### Milestone 1.8: Memberlist Delegates
**Duration: 1 week**

#### Files to Port
```
delegate.go            → Delegates/IDelegate.cs
event_delegate.go      → Delegates/IEventDelegate.cs
merge_delegate.go      → Delegates/IMergeDelegate.cs
conflict_delegate.go   → Delegates/IConflictDelegate.cs
alive_delegate.go      → Delegates/IAliveDelegate.cs
ping_delegate.go       → Delegates/IPingDelegate.cs
```

#### Implementation Tasks
- [ ] Define all delegate interfaces
- [ ] Document delegate responsibilities
- [ ] Create default no-op implementations
- [ ] Integrate delegates into memberlist lifecycle

#### Test Requirements
- [ ] Unit tests: Delegate method invocations
- [ ] Tests: Multiple delegates working together

---

### 🎯 Phase 1 Completion Criteria

**Functional Requirements:**
- ✅ Memberlist can form a cluster
- ✅ Nodes can join and leave gracefully
- ✅ Failure detection works reliably
- ✅ Gossip propagates state changes
- ✅ Encryption protects messages

**Quality Requirements:**
- ✅ Unit test coverage >75%
- ✅ Integration tests with multi-node clusters
- ✅ No known critical bugs
- ✅ Performance benchmarks baseline established

---

## 🔷 Phase 2: Serf Core Implementation
**Duration: 10-12 weeks**

### Milestone 2.1: Serf Foundation
**Duration: 2 weeks**

#### Files to Port
```
serf.go                → Serf/Serf.cs
config.go              → Serf/SerfConfig.cs
lamport.go             → Serf/LamportClock.cs
```

#### Implementation Tasks
- [ ] `Serf` main class structure
- [ ] Three Lamport clocks (member, event, query)
- [ ] `SerfConfig` with all options
- [ ] Member state tracking
- [ ] Integration with Memberlist
- [ ] Serf lifecycle (create, start, stop, shutdown)

#### Test Requirements
- [ ] Unit tests: Lamport clock operations
- [ ] Tests: Configuration validation
- [ ] Tests: Serf lifecycle
- [ ] Integration tests: Basic Serf cluster

---

### Milestone 2.2: Event System Foundation
**Duration: 2 weeks**

#### Files to Port
```
event.go               → Events/Event.cs
event_delegate.go      → Events/SerfEventDelegate.cs
```

#### Implementation Tasks
- [ ] Event type enumeration
- [ ] `IEvent` interface
- [ ] `MemberEvent` class
- [ ] `UserEvent` class
- [ ] Event channel creation
- [ ] Event delegate implementation

#### Test Requirements
- [ ] Unit tests: Event creation
- [ ] Tests: Event channel flow
- [ ] Integration tests: Member join/leave events

---

### Milestone 2.3: Event Coalescence
**Duration: 2 weeks**

#### Files to Port
```
coalesce.go            → Events/EventCoalescer.cs
coalesce_member.go     → Events/MemberEventCoalescer.cs
coalesce_user.go       → Events/UserEventCoalescer.cs
```

#### Implementation Tasks
- [ ] Base coalescer logic
- [ ] Coalesce period timer
- [ ] Quiescent period detection
- [ ] Member event batching
- [ ] User event batching

#### Test Requirements
- [ ] Unit tests: Coalesce timing
- [ ] Tests: Event batching correctness
- [ ] Integration tests: Rapid member changes

---

### Milestone 2.4: Serf Messages & Delegate
**Duration: 2 weeks**

#### Files to Port
```
messages.go            → Serf/Messages/SerfMessages.cs
delegate.go            → Serf/Delegates/SerfDelegate.cs
```

#### Implementation Tasks
- [ ] Serf message types
- [ ] Message encoding/decoding
- [ ] Tag encoding/decoding
- [ ] `SerfDelegate` implementing `IDelegate`
- [ ] Intent handling
- [ ] Rebroadcast logic

#### Test Requirements
- [ ] Unit tests: Message serialization
- [ ] Tests: Tag encoding/decoding
- [ ] Integration tests: Cross-node messages

---

### Milestone 2.5: User Events
**Duration: 1 week**

#### Implementation Tasks
- [ ] User event broadcasting
- [ ] User event buffer
- [ ] User event TTL/expiration
- [ ] User event filtering

#### Test Requirements
- [ ] Unit tests: User event lifecycle
- [ ] Integration tests: Event propagation

---

### Milestone 2.6: Query System
**Duration: 3 weeks**

#### Files to Port
```
query.go               → Queries/Query.cs
internal_query.go      → Queries/QueryResponse.cs
```

#### Implementation Tasks
- [ ] `Query` class with all options
- [ ] Query manager
- [ ] Query broadcasting
- [ ] Query filtering (node names, tags)
- [ ] Query response collection
- [ ] Query timeout handling
- [ ] Query relay mechanism

#### Test Requirements
- [ ] Unit tests: Query creation
- [ ] Tests: Filter matching
- [ ] Tests: Response collection
- [ ] Integration tests: Query across cluster

---

### Milestone 2.7: Network Coordinates (Vivaldi)
**Duration: 2 weeks**

#### Files to Port
```
coordinate/coordinate.go → Coordinate/Coordinate.cs
coordinate/client.go     → Coordinate/CoordinateClient.cs
ping_delegate.go         → Serf/Delegates/SerfPingDelegate.cs
```

#### Implementation Tasks
- [ ] `Coordinate` class
- [ ] Distance calculation
- [ ] Coordinate update (Vivaldi algorithm)
- [ ] Coordinate client
- [ ] RTT measurement integration
- [ ] Coordinate cache

#### Test Requirements
- [ ] Unit tests: Distance calculation
- [ ] Tests: Coordinate update algorithm
- [ ] Integration tests: Multi-node convergence

---

### 🎯 Phase 2 Completion Criteria

**Functional Requirements:**
- ✅ Serf wraps Memberlist
- ✅ Events propagate correctly
- ✅ User events work
- ✅ Queries work with responses
- ✅ Coordinates estimate RTT

**Quality Requirements:**
- ✅ Unit test coverage >75%
- ✅ Integration tests pass
- ✅ Event ordering verified

---

## 🔶 Phase 3: Advanced Features
**Duration: 6-8 weeks**

### Milestone 3.1: Snapshot & Persistence
**Duration: 3 weeks**

#### Files to Port
```
snapshot.go            → Snapshot/Snapshotter.cs
```

#### Implementation Tasks
- [ ] Snapshot file format
- [ ] Event recording
- [ ] Clock value persistence
- [ ] Periodic flush mechanism
- [ ] Snapshot compaction
- [ ] Snapshot recovery on startup

#### Test Requirements
- [ ] Unit tests: Snapshot write/read
- [ ] Integration tests: Restart recovery
- [ ] Tests: Compaction logic

---

### Milestone 3.2: Key Management
**Duration: 2 weeks**

#### Files to Port
```
keymanager.go          → KeyManagement/KeyManager.cs
```

#### Implementation Tasks
- [ ] Key installation via query
- [ ] Key removal via query
- [ ] Key list query
- [ ] Primary key management

#### Test Requirements
- [ ] Unit tests: Key operations
- [ ] Integration tests: Cluster-wide key rotation

---

### Milestone 3.3: Command-Line Interface
**Duration: 3 weeks**

#### Files to Port
```
cmd/serf/command/      → Cli/Commands/
```

#### Implementation Tasks
- [ ] CLI framework setup
- [ ] Agent command
- [ ] Join command
- [ ] Leave command
- [ ] Members command
- [ ] Event command
- [ ] Query command
- [ ] Keys command
- [ ] Configuration file support

#### Test Requirements
- [ ] Integration tests: Each command
- [ ] Tests: Configuration parsing

---

### 🎯 Phase 3 Completion Criteria

**Functional Requirements:**
- ✅ Snapshots persist cluster state
- ✅ Key rotation works
- ✅ CLI fully operational

---

## 🧪 Phase 4: Testing & Hardening
**Duration: 6-8 weeks**

### Milestone 4.1: Comprehensive Unit Testing
**Duration: 2 weeks**

#### Objectives
- Achieve >80% code coverage
- Test all edge cases

#### Testing Categories
- [ ] Memberlist: All protocol operations
- [ ] State Management: Concurrent access
- [ ] Security: Encryption edge cases
- [ ] Serf Events: Event ordering
- [ ] Queries: Timeout, filtering
- [ ] Coordinates: Algorithm correctness

---

### Milestone 4.2: Integration Testing
**Duration: 2 weeks**

#### Test Scenarios
- [ ] 2-node cluster: Join, leave, failure
- [ ] 3-node cluster: Gossip propagation
- [ ] 5-node cluster: Scale verification
- [ ] Network partition handling
- [ ] Rolling restarts
- [ ] Key rotation
- [ ] Event storms

---

### Milestone 4.3: Chaos & Stress Testing
**Duration: 2 weeks**

#### Chaos Scenarios
- [ ] Random packet drops
- [ ] Network latency injection
- [ ] Node crashes
- [ ] Message reordering
- [ ] CPU starvation

#### Stress Scenarios
- [ ] Large clusters (100+ nodes)
- [ ] High churn (rapid join/leave)
- [ ] Message flood
- [ ] Long-running stability (24+ hours)

---

### Milestone 4.4: Performance Optimization
**Duration: 2 weeks**

#### Tasks
- [ ] Profile CPU hotspots
- [ ] Profile memory allocations
- [ ] Optimize serialization
- [ ] Use `Span<T>` for zero-copy
- [ ] Pool buffers with `ArrayPool<T>`

#### Performance Targets
- [ ] Message latency <10ms (p99)
- [ ] Join cluster <2 seconds
- [ ] Failure detection <5 seconds
- [ ] Memory <100MB per 1000 nodes

---

### 🎯 Phase 4 Completion Criteria

**Quality Requirements:**
- ✅ Unit test coverage >80%
- ✅ All integration tests pass
- ✅ Chaos tests stable
- ✅ Performance targets met
- ✅ No known critical bugs

---

## 📚 Phase 5: Documentation
**Duration: 3-4 weeks**

### Milestone 5.1: Code Documentation
**Duration: 1 week**

#### Tasks
- [ ] XML documentation for all public APIs
- [ ] Document thread-safety guarantees
- [ ] Document configuration options
- [ ] Code examples

---

### Milestone 5.2: User Documentation
**Duration: 2 weeks**

#### Documentation to Create
- [ ] **README.md**: Quick start
- [ ] **ARCHITECTURE.md**: Design and structure
- [ ] **PORTING_NOTES.md**: Differences from Go
- [ ] **CONFIGURATION.md**: All config options
- [ ] **API_REFERENCE.md**: Public API guide
- [ ] **TUTORIALS.md**: Examples
- [ ] **TROUBLESHOOTING.md**: Common issues
- [ ] **PERFORMANCE.md**: Tuning guide

---

### Milestone 5.3: Final Validation
**Duration: 1 week**

#### Tasks
- [ ] Full end-to-end test suite run
- [ ] Protocol compatibility verification
- [ ] Security audit
- [ ] Performance regression check
- [ ] Code cleanup

---

## 📈 Progress Tracking

### Current Phase: Phase 0 - Planning
### Overall Progress: 0%

### Next Immediate Steps:
1. Complete project structure setup
2. Add core dependencies
3. Begin POC implementations
4. Set up testing framework

---

## 🎯 Success Metrics

### Functional Completeness
- [ ] All Go Serf features implemented
- [ ] Wire protocol compatible
- [ ] All tests ported and passing

### Quality Metrics
- [ ] >80% code coverage
- [ ] Zero critical bugs
- [ ] Performance within 20% of Go version

### Documentation
- [ ] All public APIs documented
- [ ] Complete user guide
- [ ] Migration guide available

---

## 📞 Team & Resources

### Required Skills
- C# / .NET expertise
- Distributed systems knowledge
- Network programming
- Testing and QA

### Estimated Effort
- **6-12 months** for 2-3 developers
- **1 QA engineer** for testing
- **Part-time tech writer** for docs

---

## 🔄 Review & Update Schedule

- **Weekly**: Progress review
- **Bi-weekly**: Milestone assessment
- **Monthly**: Phase completion review
- **Quarterly**: Overall project health check

---

## 📝 Notes

This is a living document. Update checkpoints as they are completed and adjust timelines based on actual progress.

**Last Updated**: 2025-10-14
