# Serf Agent Port - TDD Implementation Checklist

**Objective:** Port the complete Serf Agent from Go to C# with full test coverage using strict TDD methodology.

**Timeline:** 10 weeks, 8 phases  
**Total Estimated Tests:** ~250 tests  
**Total Estimated Lines:** ~8,000 implementation + ~10,000 tests

---

## Overview

### Components to Port
1. **RPC Client** (`serf/client/rpc_client.go`) - 859 lines
2. **Agent Core** (`cmd/serf/command/agent/agent.go`) - 457 lines  
3. **Configuration** (`cmd/serf/command/agent/config.go`) - 625 lines
4. **IPC Server** (`cmd/serf/command/agent/ipc.go`) - 1094 lines
5. **Event Handlers** (`cmd/serf/command/agent/event_handler.go`) - 193 lines
6. **CLI Commands** (`cmd/serf/command/agent/command.go`) - Large file
7. **Supporting Files** (invoke.go, log_writer.go, mdns.go, etc.)

### C# Technology Stack
- **Network:** `TcpClient`, `TcpListener`
- **Serialization:** `MessagePack-CSharp`
- **Async:** `Task`, `Channel<T>`, `CancellationToken`
- **Concurrency:** `SemaphoreSlim`, `ConcurrentDictionary`
- **Logging:** `Microsoft.Extensions.Logging`
- **Config:** `System.Text.Json`
- **Testing:** `xUnit`, `Moq`

---

## Phase 1: RPC Client Foundation (Week 1)

**Goal:** Core RPC client with connection, handshake, authentication, and message encoding.

### Test Groups (15 tests)
- Connection and Handshake: 5 tests
- Authentication: 3 tests
- Sequence Number Management: 3 tests
- Message Encoding/Decoding: 4 tests

### Implementation Files
```
NSerf/NSerf/Client/
├── RpcClient.cs (~400 lines)
├── RpcConfig.cs (~50 lines)
├── RpcProtocol.cs (~100 lines)
├── SeqHandler.cs (~50 lines)
└── SeqCallback.cs (~30 lines)
```

**Key Details:**
- Background task for listen loop
- Thread-safe sequence numbers with `Interlocked.Increment`
- MsgPack encoding/decoding
- Proper disposal pattern

---

## Phase 2: RPC Client Commands (Week 2)

**Goal:** All RPC commands - Members, Join, Leave, Events, Keys, Queries.

### Test Groups (29 tests)
- Membership Commands: 8 tests
- Event Commands: 4 tests
- Key Management: 8 tests
- Query Commands: 5 tests
- Other Commands: 4 tests

### Implementation Files
```
NSerf/NSerf/Client/
├── RpcClient.cs (+500 lines)
├── RpcRequests.cs (~200 lines)
├── RpcResponses.cs (~200 lines)
└── Handlers/ (MonitorHandler, StreamHandler, QueryHandler)
```

---

## Phase 3: Streaming Operations (Week 3)

**Goal:** Monitor, Stream, Stop for continuous data flow.

### Test Groups (17 tests)
- Monitor (Log Streaming): 6 tests
- Event Streaming: 8 tests
- Stop Operations: 3 tests

### Implementation Focus
- `Channel<T>` for streaming
- Proper cleanup in handlers
- Channel backpressure handling
- Thread-safe handler registration

---

## Phase 4: Agent Configuration (Week 4)

**Goal:** Configuration parsing, validation, and persistence.

### Test Groups (33 tests)
- Default Configuration: 3 tests
- JSON Loading: 8 tests
- Tags File Persistence: 5 tests
- Keyring File Loading: 4 tests
- Configuration Validation: 8 tests
- Event Scripts Parsing: 5 tests

### Implementation Files
```
NSerf/NSerf/Agent/
├── AgentConfig.cs (~300 lines)
├── SerfConfig.cs (~150 lines)
├── EventScript.cs (~100 lines)
├── EventFilter.cs (~80 lines)
├── ConfigValidator.cs (~200 lines)
└── ConfigLoader.cs (~150 lines)
```

---

## Phase 5: Agent Core (Week 5)

**Goal:** Agent wrapper managing Serf lifecycle and event handling.

### Test Groups (31 tests)
- Agent Lifecycle: 8 tests
- Event Handler Registration: 6 tests
- Serf Operations: 8 tests
- Tags Management: 5 tests
- Keyring Management: 4 tests

### Implementation Files
```
NSerf/NSerf/Agent/
├── Agent.cs (~500 lines)
├── IEventHandler.cs (~20 lines)
└── AgentState.cs (~20 lines)
```

**Key Details:**
- `Channel<Event>` for event delivery
- Background task for event loop
- Lock-free event handler management
- Tags/keyring file persistence

---

## Phase 6: IPC/RPC Server (Week 6-7)

**Goal:** Server-side RPC handler processing client requests.

### Test Groups (~60 tests)
- IPC Server Lifecycle: 6 tests
- Handshake Handling: 5 tests
- Authentication: 4 tests
- Members Commands: 8 tests
- Join/Leave Commands: 6 tests
- Event Commands: 5 tests
- Key Management Commands: 8 tests
- Query Commands: 8 tests
- Stream/Monitor Commands: 6 tests
- Stop Command: 2 tests
- Stats/Coordinate: 2 tests

### Implementation Files
```
NSerf/NSerf/Agent/
├── AgentIpc.cs (~600 lines)
├── IpcClient.cs (~200 lines)
├── IpcEventStream.cs (~150 lines)
├── IpcLogStream.cs (~80 lines)
└── IpcQueryResponseStream.cs (~100 lines)
```

**Critical Features:**
- Concurrent client handling
- MsgPack request/response protocol
- Event streaming to clients
- Query ack/response handling

---

## Phase 7: Event Handlers & Script Execution (Week 8)

**Goal:** Script execution system for events.

### Test Groups (25 tests)
- Event Filter Parsing: 5 tests
- Event Filter Matching: 6 tests
- Script Execution: 8 tests
- Environment Variables: 4 tests
- Query Response Handling: 2 tests

### Implementation Files
```
NSerf/NSerf/Agent/
├── ScriptEventHandler.cs (~150 lines)
├── EventScript.cs (update)
├── EventFilter.cs (update)
└── ScriptInvoker.cs (~200 lines)
```

**Platform Considerations:**
- Windows: PowerShell/cmd.exe
- Linux: bash/sh
- Cross-platform path handling
- Process lifecycle management

---

## Phase 8: CLI Commands & Integration (Week 9-10)

**Goal:** Complete CLI and end-to-end integration tests.

### Test Groups (~40 tests)
- Agent Command: 8 tests
- Log Management: 5 tests
- Signal Handling: 4 tests
- Reload Configuration: 6 tests
- mDNS Discovery: 5 tests
- Integration Tests: 12 tests

### Implementation Files
```
NSerf/NSerf/Agent/
├── Command.cs (~400 lines)
├── LogWriter.cs (~100 lines)
├── GatedWriter.cs (~50 lines)
├── AgentMdns.cs (~150 lines)
└── Util.cs (~50 lines)
```

### Integration Test Scenarios
1. Full agent lifecycle with RPC client
2. Multi-node cluster formation
3. Event propagation and script execution
4. Graceful shutdown and restart
5. Tag persistence across restarts
6. Key rotation without downtime
7. Query broadcast and response collection
8. Monitor and stream concurrent access
9. Configuration reload
10. Failure recovery scenarios
11. Load testing (100+ concurrent RPC clients)
12. Protocol compatibility with Go agent

---

## Testing Strategy

### Unit Tests
- Each phase has isolated unit tests
- Mock external dependencies (Serf, file system, network)
- Fast execution (<1s per test)
- 100% code coverage target

### Integration Tests
- Phase 8 focuses on integration
- Real network connections
- Multi-process scenarios
- Real file I/O
- Slower but comprehensive

### Cross-Platform Tests
- Run on Windows and Linux
- Verify path handling
- Test script execution on both platforms
- CI/CD pipeline validation

---

## Code Quality Standards

### C# Idioms
- Async/await for I/O operations
- IDisposable for resource management
- CancellationToken for cancellation
- Nullable reference types enabled
- XML documentation on public APIs

### Error Handling
- Specific exception types
- Meaningful error messages
- Proper exception chaining
- Log errors with context

### Performance
- Minimize allocations
- Use `Span<T>` where appropriate
- Efficient collections (`ConcurrentDictionary`, etc.)
- Async I/O throughout

---

## Progress Tracking

### Phase Completion Checklist
- [ ] Phase 1: RPC Client Foundation (Week 1)
- [ ] Phase 2: RPC Client Commands (Week 2)
- [ ] Phase 3: Streaming Operations (Week 3)
- [ ] Phase 4: Agent Configuration (Week 4)
- [ ] Phase 5: Agent Core (Week 5)
- [ ] Phase 6: IPC/RPC Server (Week 6-7)
- [ ] Phase 7: Event Handlers (Week 8)
- [ ] Phase 8: CLI & Integration (Week 9-10)

### Test Count by Phase
| Phase | Unit Tests | Integration Tests | Total |
|-------|-----------|------------------|-------|
| 1 | 15 | 0 | 15 |
| 2 | 29 | 0 | 29 |
| 3 | 17 | 0 | 17 |
| 4 | 33 | 0 | 33 |
| 5 | 31 | 0 | 31 |
| 6 | 60 | 0 | 60 |
| 7 | 25 | 0 | 25 |
| 8 | 28 | 12 | 40 |
| **Total** | **238** | **12** | **250** |

---

## Deliverables

### Phase 1-3: RPC Client Library
- Complete RPC client matching Go implementation
- All commands functional
- Streaming operations working
- Comprehensive test suite

### Phase 4-5: Agent Core
- Configuration system
- Agent lifecycle management
- Event handling system
- Tags/keyring persistence

### Phase 6: IPC Server
- Server-side RPC handling
- Protocol compatibility with Go client
- Concurrent client support
- Event/log streaming

### Phase 7-8: Complete Agent
- Script execution
- CLI commands
- mDNS discovery
- Full integration tests
- Cross-platform support

---

## Success Criteria

1. ✅ All 250+ tests passing
2. ✅ Protocol compatible with Go Serf agent
3. ✅ Can form cluster with Go agents
4. ✅ Can use existing Go RPC clients
5. ✅ Performance within 20% of Go implementation
6. ✅ Works on Windows and Linux
7. ✅ Documentation complete
8. ✅ CI/CD pipeline green

---

## Next Steps

1. **Review this checklist** with team
2. **Set up project structure** in NSerf solution
3. **Create Phase 1 detailed test plan** (RED)
4. **Begin Phase 1 implementation** (GREEN)
5. **Iterate through phases** following TDD

---

## References

- Go Implementation: `serf/cmd/serf/command/agent/`
- RPC Client: `serf/client/rpc_client.go`
- Protocol: `serf/client/const.go`
- Existing NSerf: `NSerf/NSerf/Serf/`
- Agent Documentation: `NSerf/agent.md`
- RPC Client Documentation: `NSerf/rpc_client.md`

---

**Note:** This is a high-level checklist. Each phase will have a detailed test specification document created during the RED phase with specific test implementations.
