# Agent Port - TDD Implementation Plan

**Methodology:** RED → GREEN → REFACTOR
- **RED**: Port Go tests first (they will fail)
- **GREEN**: Port Go code to make tests pass
- **REFACTOR**: Clean up and optimize

**Reference:** `serf/command/agent/` directory in Go repository

---

## Phase 1: Core Agent Structure ✓ (Already Completed)
**Goal:** Port basic agent wrapper and configuration

### 1.1 Configuration Tests (RED) ✅ DONE
- [x] `AgentConfigTests.cs` - Port tests from `config_test.go`
  - [x] Default configuration values
  - [x] Tags file path validation
  - [x] Keyring file path validation
  - [x] RPC address parsing
  - [x] Log level validation
  - [x] Merge with Serf config

### 1.2 Configuration Implementation (GREEN) ✅ DONE
- [x] `AgentConfig.cs` - Port from `config.go`
  - [x] All configuration properties
  - [x] Default values
  - [x] Validation logic

### 1.3 Agent Core Tests (RED) ✅ DONE
- [x] `SerfAgentTests.cs` - Port tests from `agent_test.go` (15 tests written)
  - [x] Agent creation and initialization
  - [x] Start/stop lifecycle
  - [x] Event handler registration  
  - [x] User events, queries, join/leave
  - [x] Tags file persistence

### 1.4 Agent Core Implementation (GREEN) ✅ DONE (15/15 tests passing)
- [x] `SerfAgent.cs` - Port from `agent.go` (455 lines)
  - [x] CreateAsync factory method
  - [x] StartAsync lifecycle
  - [x] Event loop (event channel wired correctly)
  - [x] Handler registration/deregistration
  - [x] DisposeAsync cleanup
  - [x] Tags/keyring file loading
  - [x] Convenience methods (Join, Leave, UserEvent, Query, etc.)
  - [x] **Fixed: Initial MemberJoin event emission**

---

## Phase 2: Event Handling System
**Goal:** Port event handler patterns and built-in handlers

### 2.1 Event Handler Tests (RED)
- [ ] `EventHandlerTests.cs` - Port from Go event handler tests
  - [ ] IEventHandler interface contract
  - [ ] DelegateEventHandler functionality
  - [ ] Multiple handlers receive same event
  - [ ] Handler exceptions don't crash event loop
  - [ ] Handler registration/deregistration
  - [ ] Event filtering by type

**Files to reference:**
- `serf/command/agent/event.go`
- `serf/command/agent/event_test.go`

### 2.2 Event Handler Implementation (GREEN)
- [ ] `IEventHandler.cs` 
- [ ] `DelegateEventHandler.cs` 
- [ ] `ScriptEventHandler.cs` - NEW
  - [ ] Execute external scripts on events
  - [ ] Stdout/stderr capture
  - [ ] Environment variable injection
  - [ ] Timeout handling

### 2.3 Event Filtering Tests (RED)
- [ ] `EventFilterTests.cs`
  - [ ] Filter by event type (user, member-join, etc.)
  - [ ] Filter by member name patterns
  - [ ] Filter by tags
  - [ ] Combined filters

### 2.4 Event Filtering Implementation (GREEN)
- [ ] `EventFilter.cs` - Port filtering logic
  - [ ] Type-based filtering
  - [ ] Regex pattern matching
  - [ ] Tag matching

---

## Phase 3: Tags Persistence
**Goal:** Port tags file loading/saving

### 3.1 Tags Persistence Tests (RED)
- [ ] `TagsPersistenceTests.cs` - Port from Go tests
  - [ ] Load tags from JSON file on startup
  - [ ] Save tags to JSON file on change
  - [ ] Handle missing file gracefully
  - [ ] Handle corrupted file with error
  - [ ] Merge file tags with config tags
  - [ ] File not created if path not specified

**Files to reference:**
- `serf/command/agent/agent.go` (loadTags/saveTags functions)

### 3.2 Tags Persistence Implementation (GREEN)
- [x] LoadTagsFromFileAsync in `SerfAgent.cs` ✓ (already created)
- [x] SaveTagsToFileAsync in `SerfAgent.cs` ✓ (already created)
- [ ] Error handling improvements
- [ ] Atomic file writes (temp file + rename)

---

## Phase 4: Keyring Management
**Goal:** Port encryption keyring file loading

### 4.1 Keyring Tests (RED)
- [ ] `KeyringPersistenceTests.cs`
  - [ ] Load keyring from JSON file
  - [ ] Keys are base64-encoded 32-byte keys
  - [ ] First key becomes primary encryption key
  - [ ] Handle missing file gracefully
  - [ ] Invalid key format returns error
  - [ ] Multiple keys supported

**Files to reference:**
- `serf/command/agent/keyring.go`
- `serf/command/agent/keyring_test.go`

### 4.2 Keyring Implementation (GREEN)
- [x] LoadKeyringFromFileAsync in `SerfAgent.cs` ✓ (basic version)
- [ ] Support multiple keys (not just first key)
- [ ] Keyring validation
- [ ] KeyManager integration

---

## Phase 5: Command-Line Agent (Optional)
**Goal:** Port serf CLI agent command

### 5.1 CLI Agent Tests (RED)
- [ ] `CliAgentTests.cs` - Port from `command/agent/command_test.go`
  - [ ] Parse command-line arguments
  - [ ] Start agent from config file
  - [ ] Handle signals (SIGTERM, SIGINT)
  - [ ] Graceful shutdown on signal
  - [ ] PID file creation
  - [ ] Log file configuration

**Files to reference:**
- `serf/command/agent/command.go`
- `serf/command/agent/command_test.go`
- `serf/command/agent/flag.go`

### 5.2 CLI Agent Implementation (GREEN)
- [ ] `AgentCommand.cs` - Port CLI command handler
  - [ ] Argument parsing
  - [ ] Config file loading
  - [ ] Signal handling (Console.CancelKeyPress)
  - [ ] PID file management
  - [ ] Logging setup

---

## Phase 6: Agent Integration Tests
**Goal:** End-to-end integration tests

### 6.1 Integration Tests (RED)
- [ ] `AgentIntegrationTests.cs`
  - [ ] Create agent, join cluster, receive events
  - [ ] Multiple agents in same process
  - [ ] Agent with IPC client communication
  - [ ] Event handler receives member-join events
  - [ ] User event propagation through agent
  - [ ] Query handling through agent
  - [ ] Tags update through SetTagsAsync
  - [ ] Graceful leave and shutdown

### 6.2 Integration Fixes (GREEN)
- [ ] Fix any integration issues discovered
- [ ] Performance tuning
- [ ] Memory leak checks

---

## Phase 7: Advanced Features
**Goal:** Port advanced agent features

### 7.1 Log Forwarding Tests (RED)
- [ ] `LogForwardingTests.cs` - Port from Go tests
  - [ ] Forward logs to syslog
  - [ ] Forward logs to file with rotation
  - [ ] Log level filtering
  - [ ] Structured logging format

**Files to reference:**
- `serf/command/agent/log.go`
- `serf/command/agent/log_test.go`

### 7.2 Log Forwarding Implementation (GREEN)
- [ ] `LogForwarder.cs` - Port log forwarding
  - [ ] Syslog integration (if needed)
  - [ ] File-based logging with rotation
  - [ ] Integration with ILogger

### 7.3 Event Script Tests (RED)
- [ ] `EventScriptTests.cs`
  - [ ] Execute script on member-join
  - [ ] Execute script on user event
  - [ ] Script receives event data via stdin
  - [ ] Script timeout kills process
  - [ ] Environment variables contain event metadata

**Files to reference:**
- `serf/command/agent/event.go` (invokeEventScript)

### 7.4 Event Script Implementation (GREEN)
- [ ] `ScriptEventHandler.cs` - Execute external scripts
  - [ ] Process spawning
  - [ ] Stdin/stdout handling
  - [ ] Timeout enforcement
  - [ ] Environment variable injection

---

## Phase 8: Snapshot Integration
**Goal:** Ensure agent works with snapshot recovery

### 8.1 Snapshot Tests (RED)
- [ ] `AgentSnapshotTests.cs`
  - [ ] Agent restarts and loads snapshot
  - [ ] Members restored from snapshot
  - [ ] Event handlers still work after restore
  - [ ] Coordinate data preserved

### 8.2 Snapshot Fixes (GREEN)
- [ ] Fix any snapshot integration issues
- [ ] Verify snapshot path configuration

---

## Phase 9: Documentation and Examples
**Goal:** Complete documentation

### 9.1 Documentation
- [ ] `Agent/README.md` - Usage guide
  - [ ] Quick start example
  - [ ] Configuration options
  - [ ] Event handler examples
  - [ ] IPC integration example
  - [ ] Best practices

### 9.2 Examples
- [ ] `Examples/SimpleAgent/` - Minimal agent example
- [ ] `Examples/EventHandlerAgent/` - Custom event handlers
- [ ] `Examples/IpcClientAgent/` - Agent with IPC client
- [ ] `Examples/ClusteredAgents/` - Multiple agents

---

## Phase 10: Performance and Hardening
**Goal:** Production readiness

### 10.1 Performance Tests
- [ ] `AgentPerformanceTests.cs`
  - [ ] 100+ events/second throughput
  - [ ] 10+ concurrent handlers
  - [ ] Memory stability over time
  - [ ] CPU usage under load

### 10.2 Hardening
- [ ] Exception handling audit
- [ ] Resource leak checks (using, Dispose patterns)
- [ ] Thread safety audit
- [ ] Cancellation token propagation
- [ ] Timeout configuration

---

## Test Execution Strategy

### Running Tests
```bash
# Run all agent tests
dotnet test --filter "FullyQualifiedName~NSerf.Tests.Agent"

# Run specific phase
dotnet test --filter "FullyQualifiedName~AgentConfigTests"

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Success Criteria
- [ ] All tests pass (100%)
- [ ] Code coverage > 80%
- [ ] No memory leaks detected
- [ ] Integration with existing Client/Serf code works

---

## Go Source Files Reference

**Primary files to port:**
1. `serf/command/agent/agent.go` - Main agent logic ✓
2. `serf/command/agent/config.go` - Configuration ✓
3. `serf/command/agent/event.go` - Event handling
4. `serf/command/agent/keyring.go` - Keyring management
5. `serf/command/agent/log.go` - Log forwarding
6. `serf/command/agent/command.go` - CLI command (optional)

**Test files to port:**
1. `serf/command/agent/agent_test.go` - Agent tests
2. `serf/command/agent/config_test.go` - Config tests
3. `serf/command/agent/command_test.go` - CLI tests
4. `serf/command/agent/keyring_test.go` - Keyring tests

---

## Dependencies

**Already implemented:**
- ✅ Serf.Serf - Core gossip protocol
- ✅ AgentIpc - IPC server
- ✅ IpcClient - IPC client
- ✅ EventStreamManager - Event streaming
- ✅ LogStreamManager - Log streaming

**To be implemented:**
- ⏳ ScriptEventHandler - External script execution
- ⏳ LogForwarder - Advanced logging
- ⏳ AgentCommand - CLI interface (optional)

---

## Timeline Estimate

| Phase | Estimated Time | Status |
|-------|---------------|--------|
| Phase 1: Core Agent | 2 days | ✅ DONE |
| Phase 2: Event Handling | 1 day | 🔄 IN PROGRESS |
| Phase 3: Tags Persistence | 0.5 days | 🔄 PARTIAL |
| Phase 4: Keyring | 0.5 days | 🔄 PARTIAL |
| Phase 5: CLI (Optional) | 1 day | ⏳ TODO |
| Phase 6: Integration | 1 day | ⏳ TODO |
| Phase 7: Advanced Features | 2 days | ⏳ TODO |
| Phase 8: Snapshot | 0.5 days | ⏳ TODO |
| Phase 9: Documentation | 1 day | ⏳ TODO |
| Phase 10: Hardening | 1 day | ⏳ TODO |
| **TOTAL** | **10-11 days** | |

---

## Current Status

**Completed:**
- ✅ Agent structure (SerfAgent, AgentConfig)
- ✅ Basic event handler interface
- ✅ IPC server integration
- ✅ Tags file persistence (basic)
- ✅ Keyring file loading (basic)

**Next Steps:**
1. **Phase 2.1**: Write EventHandlerTests.cs (RED)
2. **Phase 2.2**: Implement ScriptEventHandler.cs (GREEN)
3. **Phase 2.3**: Write EventFilterTests.cs (RED)
4. **Phase 2.4**: Implement EventFilter.cs (GREEN)

---

## Notes

- **Follow TDD strictly**: RED → GREEN → REFACTOR
- **Port tests verbatim** from Go where possible
- **Don't skip tests** - they catch bugs early
- **Run tests frequently** - after every implementation
- **Document deviations** from Go behavior when necessary (e.g., C# async patterns)

**Why TDD?**
- Ensures compatibility with Go implementation
- Catches regressions early
- Provides living documentation
- Makes refactoring safer
