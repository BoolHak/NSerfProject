# NSerf IPC/RPC Server Plan - TDD Approach

## Overview
Create IPC/RPC server for C# NSerf via TCP + MessagePack, following strict Test-Driven Development.

**Methodology:** 🔴 RED → 🟢 GREEN → 🔵 REFACTOR

**Reference:** `serf/cmd/serf/command/agent/ipc.go` (1,094 lines)
**Target:** `NSerf/NSerf/Client/` + tests

---

## Phase 1: Core Infrastructure (Week 1)

### 🔴 RED - Write Tests First (400 lines)
**Files:** `NSerfTests/Client/*`

- [x] Protocol constants validation tests (`IpcProtocolTests`)
- [x] Model serialization tests (20+ models) (`IpcModelsTests`)
- [x] AgentIpc server start/stop tests (`AgentIpcServerTests`)
- [x] AgentIpc handshake enforcement tests (`AgentIpcServerTests`)
- [x] Auth enforcement after handshake (`AgentIpcServerTests`)
- [x] Unsupported command error (`AgentIpcServerTests`)
- [ ] IpcClient SendAsync tests
- [ ] IpcClient concurrent write tests
- [ ] IpcClient query registration tests
- [ ] AgentIpc multiple client tests

### 🟢 GREEN - Implement (current status)
**Done:**
- `IpcProtocol.cs` - Constants implemented and validated
- `IpcModels.cs` - Request/response models implemented and validated
- `AgentIpc.cs` - TCP server + routing implemented
  - Uses `MessagePackStreamReader` with `leaveOpen: true` for bidirectional reads
  - Passes the reader to command handlers to read bodies exactly once
  - Server enforces handshake and auth as per spec
  - Concurrency: responses serialized via `_writeLock` in `IpcClientHandler`
- `AgentIpc.Commands.cs` - Implemented handlers for: members, filtered-members, join, leave, force-leave, tags, user-event, stats, key ops, coordinate (all stubbed to Serf integration, but protocol-correct)

**Completed (Phase 2):**
- ✅ `IpcClient.cs` - Full client implementation with:
  - Connection management with `MessagePackStreamReader`
  - Handshake, Auth methods
  - Command methods: GetMembers, GetStats, Join, Leave, ForceLeave, UserEvent, SetTags
  - Key management: InstallKey, UseKey, RemoveKey, ListKeys
  - Proper body reading with `ReadBodyAsync<T>`
- ✅ `IpcClientTests.cs` - 14 comprehensive tests covering:
  - Handshake happy path and unsupported version
  - Command without handshake (HandshakeRequired error)
  - Auth enforcement and invalid token
  - Sequential sends with body consumption
  - Multiple simultaneous clients
  - Disconnect and cancellation handling
  - GetMembers with body parsing
  - GetStats with body parsing
  - Join request with body
  - Leave request
- 🔧 **Key Fix:** Client consumes response bodies to keep stream in sync

**Next (Phase 3):**
- Add streaming API (monitor/stream/stop) with event callbacks
- Add query API with response aggregation

### 🔵 REFACTOR (planned)
- Extract test helpers for stream setup and reader usage
- Remove temporary Console.WriteLine in production code, keep ILogger
- Consider pooling buffers for responses

---

## Design Notes (decided)
- Use `MessagePackStreamReader` for all request reads over `NetworkStream` to avoid whole-stream consumption issues
- Pass a single reader per-connection down to handlers: `HandleXxxAsync(client, seq, reader, ct)`
- Keep `NetworkStream` open for writes; `IpcClientHandler.SendAsync` handles serialization under a lock
- Avoid passing CancellationToken to low-level MessagePack read that may close streams on cancel; rely on outer loop cancellation

## Phase 2: Client Library (Week 2)

### 🔴 RED
- [ ] `IpcClient` handshake/auth happy path tests
- [ ] Error propagation tests (Unsupported, AuthRequired, HandshakeRequired)
- [ ] Streaming API basics (monitor/stream/stop skeleton tests)

### 🟢 GREEN
- [ ] Implement `IpcClient` with reader loop + callbacks/awaitables
- [ ] Add cancellation and graceful shutdown

### 🔵 REFACTOR
- [ ] Extract common message framing helpers

## Status
- **All client/server tests passing: 72/72** ✅
  - 58 original tests (protocol, models, server, handlers)
  - 14 IpcClient tests (handshake, auth, commands, errors, concurrency)
- Warnings addressed: xUnit nullability fixed; MessagePack resolver conflicts are benign (generated)
- **Client body consumption bug fixed:** Commands that return bodies now properly consume response bodies
- **Client methods implemented:** All basic commands (members, join, leave, force-leave, tags, events, keys, stats)

---

## Phase 2: Handshake & Auth (Week 2)

### 🔴 RED - Write Tests (250 lines)
**File:** `NSerfTests/Client/HandshakeAuthTests.cs`

- [ ] Handshake with valid/invalid version
- [ ] Duplicate handshake rejection
- [ ] Commands blocked without handshake
- [ ] Auth with valid/invalid key
- [ ] Commands blocked without auth (when required)
- [ ] Commands allowed after auth

### 🟢 GREEN - Implement (80 lines)
**File:** `AgentIpc.Commands.cs`

- [ ] `HandleHandshakeAsync` - Version validation
- [ ] `HandleAuthAsync` - Key validation

### 🔵 REFACTOR
- Consolidate validation logic

---

## Phase 3: Cluster Operations (Week 2)

### 🔴 RED - Write Tests (400 lines)
**File:** `NSerfTests/Client/ClusterCommandTests.cs`

- [ ] Members returns all members
- [ ] Members converts types correctly
- [ ] MembersFiltered by tags (regex)
- [ ] MembersFiltered by status
- [ ] MembersFiltered by name
- [ ] MembersFiltered combined filters
- [ ] Join with addresses and replay flag
- [ ] Join returns joined count
- [ ] Leave calls Serf.Leave
- [ ] ForceLeave with prune flag

### 🟢 GREEN - Implement (250 lines)
**File:** `AgentIpc.Commands.cs` (continued)

- [ ] `HandleMembersAsync`
- [ ] `HandleMembersFilteredAsync`
- [ ] `FilterMembers` helper (regex matching)
- [ ] `HandleJoinAsync`
- [ ] `HandleLeaveAsync`
- [ ] `HandleForceLeaveAsync`

### 🔵 REFACTOR
- Extract filter logic to separate class

---

## Phase 4: Events & Stats (Week 3)

### 🔴 RED - Write Tests (200 lines)
**File:** `NSerfTests/Client/EventsStatsTests.cs`

- [ ] UserEvent triggers Serf event
- [ ] UpdateTags merges and deletes tags
- [ ] Stats returns cluster stats
- [ ] GetCoordinate returns node coordinate
- [ ] GetCoordinate returns null for missing node

### 🟢 GREEN - Implement (150 lines)
**File:** `AgentIpc.Commands.cs` (continued)

- [ ] `HandleEventAsync`
- [ ] `HandleTagsAsync`
- [ ] `HandleStatsAsync`
- [ ] `HandleGetCoordinateAsync`

---

## Phase 5: Key Management (Week 3)

### 🔴 RED - Write Tests (200 lines)
**File:** `NSerfTests/Client/KeyManagementTests.cs`

- [ ] InstallKey returns response from all nodes
- [ ] UseKey changes primary key
- [ ] RemoveKey removes key
- [ ] ListKeys returns all keys
- [ ] Key operations return error counts

### 🟢 GREEN - Implement (150 lines)
**File:** `AgentIpc.KeyManagement.cs`

- [ ] `HandleInstallKeyAsync`
- [ ] `HandleUseKeyAsync`
- [ ] `HandleRemoveKeyAsync`
- [ ] `HandleListKeysAsync`

---

## Phase 6: Log Streaming (Week 4)

### 🔴 RED - Write Tests (300 lines)
**File:** `NSerfTests/Client/LogStreamingTests.cs`

- [ ] Monitor streams logs to client
- [ ] Monitor filters by log level
- [ ] Monitor rejects duplicate streams
- [ ] Stop command stops log stream
- [ ] Multiple clients can monitor simultaneously
- [ ] Log stream closes on client disconnect

### 🟢 GREEN - Implement (250 lines)
**Files:**
- `Streaming/LogStream.cs` (150 lines) - Log capture and streaming
- `AgentIpc.Streaming.cs` (100 lines) - Monitor and Stop handlers

---

## Phase 7: Event Streaming (Week 4)

### 🔴 RED - Write Tests (350 lines)
**File:** `NSerfTests/Client/EventStreamingTests.cs`

- [ ] Stream filters events by type
- [ ] Stream sends member events (join/leave/fail)
- [ ] Stream sends user events
- [ ] Stream sends query events
- [ ] Stream with wildcard filter
- [ ] Stop command stops event stream
- [ ] Multiple streams with different filters
- [ ] Event stream closes on disconnect

### 🟢 GREEN - Implement (350 lines)
**Files:**
- `Streaming/EventFilter.cs` (100 lines) - Parse and match filters
- `Streaming/EventStream.cs` (150 lines) - Event capture and streaming
- `AgentIpc.Streaming.cs` (100 lines) - Stream handler

---

## Phase 8: Query System (Week 5)

### 🔴 RED - Write Tests (400 lines)
**File:** `NSerfTests/Client/QueryTests.cs`

- [ ] Query sends to filtered nodes
- [ ] Query sends to filtered tags
- [ ] Query streams acks
- [ ] Query streams responses
- [ ] Query sends done when complete
- [ ] Respond sends response to query
- [ ] Respond fails for invalid query ID
- [ ] Query timeout auto-deregisters

### 🟢 GREEN - Implement (290 lines)
**Files:**
- `Streaming/QueryResponseStream.cs` (150 lines) - Stream acks/responses
- `AgentIpc.Query.cs` (140 lines) - Query and Respond handlers

---

## Phase 9: Client Library (Week 5 - Optional)

### 🔴 RED - Write Tests (500 lines)
**File:** `NSerfTests/Client/SerfRpcClientTests.cs`

- [ ] Connect and handshake
- [ ] All command methods work
- [ ] Streaming methods return channels
- [ ] Concurrent operations are safe
- [ ] Disconnection cleanup

### 🟢 GREEN - Implement (800 lines)
**File:** `SerfRpcClient.cs`

- Connection, handshake, auth
- All 18 command methods (async)
- Streaming methods returning ChannelReader<T>
- Response matching by sequence

---

## Phase 10: Integration (Week 6)

### 🔴 RED - Write Integration Tests (600 lines)
**File:** `NSerfTests/Client/IpcIntegrationTests.cs`

- [ ] Real Serf agent with RPC enabled
- [ ] End-to-end command tests
- [ ] Multi-node cluster operations
- [ ] Streaming with real events
- [ ] Stress test with many clients

### 🟢 GREEN - Integrate with Serf (50 lines)
**File:** `NSerf/Serf/SerfConfig.cs` + `Serf.cs`

- Add RpcAddr, RpcAuthKey, RpcEnabled to config
- Start AgentIpc in Serf.CreateAsync
- Stop AgentIpc in Serf.DisposeAsync

---

## File Structure Summary

```
NSerf/NSerf/Client/
├── IpcProtocol.cs                (80)      - Constants
├── IpcModels.cs                  (500)     - Models
├── IpcClient.cs                  (200)     - Connection
├── AgentIpc.cs                   (300)     - Server
├── AgentIpc.Commands.cs          (450)     - Handlers
├── AgentIpc.KeyManagement.cs     (150)     - Keys
├── AgentIpc.Streaming.cs         (200)     - Streams
├── AgentIpc.Query.cs             (140)     - Queries
├── Streaming/
│   ├── LogStream.cs              (150)
│   ├── EventFilter.cs            (100)
│   ├── EventStream.cs            (150)
│   └── QueryResponseStream.cs    (150)
└── SerfRpcClient.cs (optional)   (800)

NSerfTests/Client/
├── IpcCoreTests.cs               (400)
├── HandshakeAuthTests.cs         (250)
├── ClusterCommandTests.cs        (400)
├── EventsStatsTests.cs           (200)
├── KeyManagementTests.cs         (200)
├── LogStreamingTests.cs          (300)
├── EventStreamingTests.cs        (350)
├── QueryTests.cs                 (400)
├── SerfRpcClientTests.cs         (500)
└── IpcIntegrationTests.cs        (600)
```

**Server:** ~2,620 lines  
**Client:** ~800 lines (optional)  
**Tests:** ~3,600 lines

---

## Timeline

| Phase | Duration | Tests | Implementation |
|-------|----------|-------|----------------|
| 1. Core Infrastructure | Week 1 | 400 | 850 |
| 2. Handshake & Auth | Week 2 | 250 | 80 |
| 3. Cluster Operations | Week 2 | 400 | 250 |
| 4. Events & Stats | Week 3 | 200 | 150 |
| 5. Key Management | Week 3 | 200 | 150 |
| 6. Log Streaming | Week 4 | 300 | 250 |
| 7. Event Streaming | Week 4 | 350 | 350 |
| 8. Query System | Week 5 | 400 | 290 |
| 9. Client (optional) | Week 5 | 500 | 800 |
| 10. Integration | Week 6 | 600 | 50 |
| **Total** | **6 weeks** | **3,600** | **3,220** |

---

## TDD Best Practices

### Test-First Rules
1. **Never write production code without a failing test**
2. **Write only enough test to fail**
3. **Write only enough code to pass the failing test**
4. **Refactor only when all tests pass**

### Test Structure
```csharp
// Arrange
var server = await CreateServerAsync();
var client = await ConnectAndAuthAsync(server.Port);

// Act
var response = await ExecuteCommandAsync(client, command);

// Assert
Assert.AreEqual(expected, response.Value);
```

### Mock Strategy
- Mock Serf for unit tests
- Use real Serf for integration tests
- Create test helpers for common operations

### Coverage Goals
- **Unit tests:** >90% code coverage
- **Integration tests:** All commands end-to-end
- **Stress tests:** Concurrency and load

---

## Success Criteria

- [ ] All tests written before implementation
- [ ] All tests pass
- [ ] >90% code coverage
- [ ] Zero new dependencies
- [ ] Thread-safe concurrent operations
- [ ] Compatible with Go serf client (protocol level)
- [ ] Clean, maintainable code
- [ ] Comprehensive XML documentation

---

## Dependencies

**Already in NSerf:**
- ✅ MessagePack (3.1.4)
- ✅ Microsoft.Extensions.Logging

**Standard Library:**
- ✅ System.Net.Sockets
- ✅ System.Threading.Channels
- ✅ System.Text.RegularExpressions

**No new dependencies!**
