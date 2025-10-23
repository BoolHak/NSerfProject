# Serf Agent TDD Test Phases - Complete Test Plan

**Total Duration:** 10 weeks | **Total Tests:** 309 | **Methodology:** Test-Driven Development

⚠️ **IMPORTANT:** Phases 1-8 build **Agent & Client layers** only. The Serf library core (gossip, membership, events, queries) is already fully implemented in `NSerf/Serf/` and `NSerf/Memberlist/`.

See **PHASE0_FOUNDATION_STATUS.md** for complete architecture analysis.

---

## Phase Files Created

| Phase | File | Duration | Tests | Focus |
|-------|------|----------|-------|-------|
| 1 | PHASE1_RPC_CLIENT_TESTS.md | Week 1 | 19 | Connection, handshake, auth, encoding |
| 2 | PHASE2_RPC_COMMANDS.md | Week 2 | 33 | All RPC commands |
| 3 | PHASE3_STREAMING_OPERATIONS.md | Week 3 | 22 | Monitor, Stream, Stop |
| 4 | PHASE4_AGENT_CONFIGURATION.md | Week 4 | 41 | Config parsing & validation |
| 5 | PHASE5_AGENT_CORE.md | Week 5 | 38 | Agent lifecycle & events |
| 6 | PHASE6_IPC_SERVER.md | Weeks 6-7 | 69 | Server-side RPC |
| 7 | PHASE7_EVENT_HANDLERS.md | Week 8 | 35 | Script execution |
| 8 | PHASE8_CLI_INTEGRATION.md | Weeks 9-10 | 52 | CLI & integration |
| **Total** | | **10 weeks** | **309** | **Complete Agent** |

---

## Quick Start Guide

### Week 1: RPC Client Foundation
```bash
cd NSerfTests/Client
# Create RpcClientTests.cs
# Copy Test 1.1.1 from PHASE1_RPC_CLIENT_TESTS.md
dotnet test
# ❌ RED - RpcClient doesn't exist yet
```

Create `NSerf/Client/RpcClient.cs` with minimal implementation to pass test.

### Week 2: Add Commands
Open PHASE2_RPC_COMMANDS.md and implement each command group:
1. Membership commands (8 tests)
2. Event commands (4 tests)
3. Key management (8 tests)
4. Queries (5 tests)
5. Misc (4 tests)

### Week 3: Streaming
Implement handlers for continuous data flow:
- MonitorHandler for logs
- StreamHandler for events
- QueryHandler for query responses

### Week 4: Configuration
Build configuration system:
- JSON parsing
- Validation
- Tags/keyring persistence
- Event script parsing

### Week 5: Agent Core
Wrap Serf with Agent:
- Lifecycle management
- Event handler registration
- Tags/keyring file I/O

### Weeks 6-7: IPC Server
Server-side implementation:
- TCP listener
- Request/response handling
- All command handlers
- Client management

### Week 8: Event Handlers
Script execution:
- Platform-specific invocation
- Environment variables
- Query responses via stdout

### Weeks 9-10: Integration
Complete the agent:
- CLI commands
- Signal handling
- mDNS discovery
- Full integration tests
- Protocol compatibility

---

## Test Count Breakdown

```
Phase 1: Connection & Protocol     19 tests (+4 from review)
Phase 2: RPC Commands             33 tests (+4 from review)
Phase 3: Streaming                22 tests (+5 from review)
─────────────────────────────────────────
Subtotal: RPC Client              74 tests

Phase 4: Configuration            41 tests (+8 from review)
Phase 5: Agent Core              38 tests (+7 from review)
─────────────────────────────────────────
Subtotal: Agent Foundation        79 tests

Phase 6: IPC Server              69 tests (+9 from review)
Phase 7: Event Handlers          35 tests (+10 from review)
Phase 8: CLI & Integration       52 tests (+12 from review)
─────────────────────────────────────────
Subtotal: Server & Integration   156 tests

═════════════════════════════════════════
TOTAL                           309 tests
```

**Notes:** 
- Phase 1: 15→19 tests (+4 from Go verification, 1 deferred)
- Phase 2: 29→33 tests (+4 from Go verification)
- Phase 3: 17→22 tests (+5 from Go verification)
- Phase 4: 33→41 tests (+8 from Go verification)
- Phase 5: 31→38 tests (+7 from Go verification)
- Phase 6: 60→69 tests (+9 from Go verification)
- Phase 7: 25→35 tests (+10 from Go verification)
- Phase 8: 40→52 tests (+12 from Go verification)
- **ALL PHASES VERIFIED!** ✅
- See verification reports for details

---

## File Structure After Completion

```
NSerf/
├── NSerf/
│   ├── Client/ (Phase 1-3)
│   │   ├── RpcClient.cs
│   │   ├── RpcConfig.cs
│   │   ├── RpcProtocol.cs
│   │   ├── StreamHandle.cs
│   │   ├── Requests/ (14 files)
│   │   ├── Responses/ (7 files)
│   │   ├── Handlers/ (3 files)
│   │   └── Exceptions/ (4 files)
│   │
│   └── Agent/ (Phase 4-8)
│       ├── AgentConfig.cs
│       ├── Agent.cs
│       ├── AgentIpc.cs
│       ├── IpcClient.cs
│       ├── ScriptEventHandler.cs
│       ├── Command.cs
│       └── ... (20+ more files)
│
├── NSerfTests/
│   ├── Client/ (61 tests)
│   │   └── ... (11 test files)
│   │
│   └── Agent/ (189 tests)
│       └── ... (21 test files)
│
└── Documentation/
    ├── AGENT_PORT_TDD_CHECKLIST.md (master plan)
    ├── AGENT_PORT_SUMMARY.md (getting started)
    ├── PROJECT_STRUCTURE.md (file structure)
    ├── PHASE1_RPC_CLIENT_TESTS.md
    ├── PHASE2_RPC_COMMANDS.md
    ├── PHASE3_STREAMING_OPERATIONS.md
    ├── PHASE4_AGENT_CONFIGURATION.md
    ├── PHASE5_AGENT_CORE.md
    ├── PHASE6_IPC_SERVER.md
    ├── PHASE7_EVENT_HANDLERS.md
    ├── PHASE8_CLI_INTEGRATION.md
    └── PHASES_OVERVIEW.md (this file)
```

---

## Key Milestones

### Milestone 1: RPC Client Complete (End of Week 3)
- ✅ Can connect to Go Serf agent
- ✅ Can execute all commands
- ✅ Streaming operations work
- ✅ 61 tests passing

### Milestone 2: Agent Foundation (End of Week 5)
- ✅ Configuration system complete
- ✅ Agent lifecycle working
- ✅ Event handlers functional
- ✅ 125 tests passing

### Milestone 3: Server Implementation (End of Week 7)
- ✅ IPC server accepts connections
- ✅ All commands handled
- ✅ Protocol compatible with Go client
- ✅ 185 tests passing

### Milestone 4: Production Ready (End of Week 10)
- ✅ Script execution cross-platform
- ✅ CLI commands complete
- ✅ Integration tests pass
- ✅ C# ↔ Go interoperability verified
- ✅ 250 tests passing
- ✅ Ready for production use

---

## Daily Workflow

### 1. RED Phase (Morning)
- Open relevant PHASE*.md file
- Read next test specification
- Create test file
- Write failing test
- Verify test fails for right reason

### 2. GREEN Phase (Afternoon)
- Implement minimal code to pass test
- Run test
- Verify test passes
- Commit: "feat: implement X (test passing)"

### 3. REFACTOR Phase (End of day)
- Clean up implementation
- Extract common patterns
- Improve naming
- Add XML documentation
- Run all tests
- Commit: "refactor: improve X"

### 4. Repeat
- Next test from phase document
- Maintain discipline: RED → GREEN → REFACTOR

---

## Common Patterns

### Async Methods
```csharp
public async Task<T> MethodAsync(params)
{
    var result = await SomeAsyncOperation();
    return result;
}
```

### Error Handling
```csharp
try
{
    await operation();
}
catch (SpecificException ex)
{
    _logger.LogError(ex, "Context");
    throw new RpcException("User message", ex);
}
```

### Disposal
```csharp
public class Resource : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await CleanupAsync();
        _tcpClient?.Dispose();
    }
}
```

---

## Progress Tracking

Use this checklist to track overall progress:

- [ ] **Phase 1 Complete** - RPC client foundation
- [ ] **Phase 2 Complete** - All RPC commands
- [ ] **Phase 3 Complete** - Streaming operations
- [ ] **Phase 4 Complete** - Configuration system
- [ ] **Phase 5 Complete** - Agent core
- [ ] **Phase 6 Complete** - IPC server
- [ ] **Phase 7 Complete** - Event handlers
- [ ] **Phase 8 Complete** - CLI & integration
- [ ] **All Tests Passing** - 250/250
- [ ] **Go Compatibility** - Verified
- [ ] **Documentation** - Complete
- [ ] **Production Ready** - ✓

---

## Getting Help

### DeepWiki Queries
When stuck, query the Go implementation:
```
@mcp:deepwiki: How does the Go RPC client handle [specific feature]?
@mcp:deepwiki: What is the handshake protocol in serf agent?
@mcp:deepwiki: How are event filters implemented in Go?
```

### Go Reference Code
- **RPC Client:** `serf/client/rpc_client.go`
- **RPC Protocol:** `serf/client/const.go`
- **Agent:** `serf/cmd/serf/command/agent/agent.go`
- **IPC Server:** `serf/cmd/serf/command/agent/ipc.go`
- **Tests:** `serf/cmd/serf/command/agent/*_test.go`

---

## Success Metrics

### Code Quality
- All tests passing (250/250)
- Code coverage >95%
- No compiler warnings
- Follows C# conventions

### Performance
- Agent startup <1s
- RPC latency <10ms
- Memory <50MB base

### Compatibility
- Protocol matches Go
- C# ↔ Go interop works
- Mixed clusters stable

---

**Ready to build! Start with Phase 1, Test 1.1.1. Let's port this agent! 🚀**
