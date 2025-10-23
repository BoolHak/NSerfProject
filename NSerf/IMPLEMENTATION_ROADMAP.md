# NSerf Implementation Roadmap - Updated Plan

**Date:** Oct 23, 2025  
**Status:** Phase 0 Complete - Foundation Verified  
**Next:** Begin Phase 1 (RPC Client)

---

## 🎯 Critical Discovery

After analyzing the current implementation, we discovered:

**✅ THE SERF LIBRARY CORE IS COMPLETE!**

- Memberlist package (93 files) - Full gossip protocol ✅
- Serf package (54 files) - Complete membership/events/queries ✅
- State machine - Correct transition logic ✅
- Managers - Transaction pattern implemented ✅
- Snapshotter - Full persistence and recovery ✅

**Phases 1-8 build the Agent and Client wrapper layers only.**

---

## 📦 What We Have (Foundation)

### **1. Memberlist Library** ✅ Complete
**Location:** `NSerf/Memberlist/` (93 files)

- Gossip protocol (SWIM + gossip)
- Failure detection with suspicion mechanism
- State synchronization (push-pull)
- Network transport (TCP/UDP)
- Encryption and key rotation
- All message types
- Broadcast queues
- Join/Leave operations
- Awareness and health scoring

**Status:** Production-ready, verified against Go implementation

---

### **2. Serf Core Library** ✅ Complete
**Location:** `NSerf/Serf/` (54 files)

#### **Main Coordinator**
- `Serf.cs` (1,234 lines) - Main orchestrator
- `Config.cs` - Complete configuration system
- All public API methods implemented

#### **State Machine** ✅
- `StateMachine/MemberStateMachine.cs` - Transition logic
- `StateMachine/TransitionResult.cs` - Result types
- Verified correct behavior:
  - Left/Failed resurrection blocked ✅
  - Refutation logic ✅
  - LTime-only updates ✅

#### **Managers** ✅
- `MemberManager.cs` - Transaction API
- `EventManager.cs` - Event broadcasting
- `ClusterCoordinator.cs` - Lifecycle management
- `IMemberManager.cs` / `IMemberStateAccessor.cs` - Interfaces

#### **Systems** ✅
- Event system (de-duplication, coalescing, buffering)
- Query system (broadcast, response collection, filtering)
- Snapshot system (persistence, recovery, compaction)
- Encryption system (KeyManager with rotation)
- Coordinate system (Vivaldi algorithm)

#### **Delegates** ✅
- Main delegate (Serf message handling)
- Event delegate (memberlist integration)
- Merge delegate (state synchronization)
- Conflict delegate (name resolution)
- Ping delegate (RTT tracking)

**Status:** Production-ready, all APIs available for Agent

---

### **3. Coordinate Package** ✅ Complete
**Location:** `NSerf/Coordinate/` (3 files)

- Vivaldi network coordinate system
- RTT estimation
- Coordinate updates

---

### **4. Metrics Package** ✅ Complete
**Location:** `NSerf/Metrics/` (3 files)

- Metrics abstraction
- NullMetrics (no-op implementation)

---

## 🚧 What We're Building (Phases 1-8)

### **Agent Package** ❌ Empty
**Location:** `NSerf/Agent/` - 0 files

**Purpose:** Command-line agent that wraps Serf library

**Phases 4-8 will create:**
```
Agent/
├── Phase 4: Configuration
│   ├── AgentConfig.cs           - Agent configuration model
│   ├── ConfigParser.cs          - JSON parsing
│   ├── ConfigMerger.cs          - Multi-file merge
│   └── ConfigValidator.cs       - Validation logic
│
├── Phase 5: Agent Core
│   ├── Agent.cs                 - Main agent wrapper
│   └── EventHandlers.cs         - Event handler management
│
├── Phase 6: IPC Server
│   ├── AgentIpc.cs              - RPC server (TCP listener)
│   ├── IpcClient.cs             - Client connection handler
│   ├── IpcEventStream.cs        - Event streaming to clients
│   ├── IpcLogStream.cs          - Log streaming to clients
│   └── IpcQueryResponseStream.cs - Query response streaming
│
├── Phase 7: Event Handlers
│   ├── ScriptEventHandler.cs   - Script execution handler
│   ├── ScriptInvoker.cs         - Process management
│   ├── EventScript.cs           - Script configuration
│   └── EventFilter.cs           - Event filtering logic
│
└── Phase 8: CLI Integration
    ├── Command.cs               - Main CLI command
    ├── LogWriter.cs             - Circular log buffer
    ├── GatedWriter.cs           - Startup log gating
    ├── AgentMdns.cs             - mDNS discovery
    └── Util.cs                  - Utility functions
```

---

### **Client Package** ❌ Empty
**Location:** `NSerf/Client/` - 0 files

**Purpose:** RPC client library for connecting to Agent

**Phases 1-3 will create:**
```
Client/
├── Phase 1: Foundation
│   ├── RpcClient.cs             - Main RPC client class
│   ├── RpcConfig.cs             - Client configuration
│   ├── RpcConnection.cs         - TCP connection management
│   └── RpcProtocol.cs           - Handshake/auth logic
│
├── Phase 2: Commands (methods in RpcClient.cs)
│   ├── MembersAsync()
│   ├── JoinAsync()
│   ├── LeaveAsync()
│   ├── ForceLeaveAsync()
│   ├── UserEventAsync()
│   ├── QueryAsync()
│   ├── InstallKeyAsync()
│   ├── UseKeyAsync()
│   ├── RemoveKeyAsync()
│   ├── ListKeysAsync()
│   ├── SetTagsAsync()
│   ├── StatsAsync()
│   └── GetCoordinateAsync()
│
└── Phase 3: Streaming (methods in RpcClient.cs)
    ├── MonitorAsync()           - Log streaming
    ├── StreamAsync()            - Event streaming
    └── StopAsync()              - Stop stream by ID
```

---

## 🔌 Integration Architecture

```
┌─────────────────────────────────────────┐
│     serf CLI Command (main.go)          │ ← User interface
└─────────────────┬───────────────────────┘
                  │
┌─────────────────▼───────────────────────┐
│     Agent Package (Phases 4-8)          │
│  ┌──────────────────────────────────┐   │
│  │ Agent.cs                         │   │ ← Wraps Serf
│  │ - Configuration management       │   │
│  │ - Event script execution         │   │
│  │ - IPC server (AgentIpc)         │   │
│  └──────────────┬───────────────────┘   │
│                 │ uses                   │
│  ┌──────────────▼───────────────────┐   │
│  │ ✅ Serf Library (EXISTS)         │   │
│  │ - Serf.cs (orchestrator)         │   │
│  │ - State machine                  │   │
│  │ - Managers (Member/Event/etc.)   │   │
│  │ - Snapshotter                    │   │
│  │ - KeyManager                     │   │
│  │ - Query system                   │   │
│  │ - Event system                   │   │
│  └──────────────┬───────────────────┘   │
│                 │ uses                   │
│  ┌──────────────▼───────────────────┐   │
│  │ ✅ Memberlist (EXISTS)           │   │
│  │ - Gossip protocol                │   │
│  │ - Failure detection              │   │
│  │ - Network transport              │   │
│  └──────────────────────────────────┘   │
└─────────────────────────────────────────┘
                  │
                  │ RPC/TCP
                  │
┌─────────────────▼───────────────────────┐
│     Client Package (Phases 1-3)         │
│  ┌──────────────────────────────────┐   │
│  │ RpcClient.cs                     │   │ ← RPC client
│  │ - TCP connection                 │   │
│  │ - MessagePack encoding           │   │
│  │ - All commands                   │   │
│  │ - Streaming                      │   │
│  └──────────────────────────────────┘   │
└─────────────────────────────────────────┘
```

---

## 📋 Updated Phase Descriptions

### **Phase 0: Foundation Verification** ✅ COMPLETE
- Analyzed existing implementation
- Verified Serf library completeness
- Confirmed state machine correctness
- Validated manager pattern
- Documented architecture
- Created compatibility matrix

**Deliverables:**
- ✅ PHASE0_FOUNDATION_STATUS.md
- ✅ ARCHITECTURE_ANALYSIS.md
- ✅ IMPLEMENTATION_ROADMAP.md (this document)

---

### **Phase 1: RPC Client Foundation** (Week 1)
**Focus:** Build TCP RPC client that connects to Agent

**Scope:**
- Create Client package structure
- Implement RpcClient.cs with TCP connection
- Add MessagePack encoding/decoding
- Implement handshake protocol
- Add authentication logic
- Test against Go agent initially

**Files to Create:**
- `Client/RpcClient.cs`
- `Client/RpcConfig.cs`
- `Client/RpcConnection.cs`
- `Client/RpcProtocol.cs`

**Tests:** 19 tests
- Connection establishment
- Handshake protocol
- Authentication
- MessagePack encoding
- Error handling

**Key Point:** Can test against Go `serf agent` before building our Agent.

---

### **Phase 2: RPC Commands** (Week 2)
**Focus:** Implement all RPC command methods

**Scope:**
- Add all command methods to RpcClient.cs
- Implement request/response handling
- Add error handling for each command
- Test each command individually

**Methods to Add:**
- Members/MembersFiltered
- Join/Leave/ForceLeave
- UserEvent
- Query
- Key management (Install/Use/Remove/List)
- Tags
- Stats/GetCoordinate

**Tests:** 33 tests (one per command + edge cases)

---

### **Phase 3: Streaming Operations** (Week 3)
**Focus:** Add streaming capabilities (monitor, events)

**Scope:**
- Implement Monitor (log streaming)
- Implement Stream (event streaming)
- Implement Stop (cancel stream)
- Add async enumerable support
- Handle stream lifecycle

**Tests:** 22 tests

---

### **Phase 4: Agent Configuration** (Week 4)
**Focus:** Build agent configuration system

**Scope:**
- Define AgentConfig model
- Implement JSON config parser
- Add multi-file config merging
- Implement validation
- Add defaults and profiles

**Files to Create:**
- `Agent/AgentConfig.cs`
- `Agent/ConfigParser.cs`
- `Agent/ConfigMerger.cs`
- `Agent/ConfigValidator.cs`

**Tests:** 41 tests

**Uses:** Nothing - standalone component

---

### **Phase 5: Agent Core** (Week 5)
**Focus:** Create Agent wrapper around Serf library

**Scope:**
- Implement Agent.cs
- Wrap Serf.CreateAsync()
- Add lifecycle methods (Create/Start/Leave/Shutdown)
- Implement event handler registration
- Add tags file persistence
- Add keyring file persistence

**Files to Create:**
- `Agent/Agent.cs`
- `Agent/EventHandlers.cs`

**Uses:**
- ✅ Serf.CreateAsync()
- ✅ serf.JoinAsync()
- ✅ serf.LeaveAsync()
- ✅ serf.ShutdownAsync()
- ✅ serf.SetTagsAsync()
- ✅ serf.KeyManager
- ✅ serf.IpcEventReader

**Tests:** 38 tests

---

### **Phase 6: IPC Server** (Weeks 6-7)
**Focus:** Implement server-side RPC handling

**Scope:**
- Create AgentIpc with TCP listener
- Implement handshake/auth
- Add command dispatch
- Implement all 19 command handlers
- Add event streaming
- Add log streaming
- Add query response streaming

**Files to Create:**
- `Agent/AgentIpc.cs` (~600 lines)
- `Agent/IpcClient.cs` (~200 lines)
- `Agent/IpcEventStream.cs` (~150 lines)
- `Agent/IpcLogStream.cs` (~80 lines)
- `Agent/IpcQueryResponseStream.cs` (~100 lines)

**Uses:**
- ✅ agent.Serf() - gets Serf instance
- ✅ serf.Members()
- ✅ serf.JoinAsync()
- ✅ serf.LeaveAsync()
- ✅ serf.UserEventAsync()
- ✅ serf.QueryAsync()
- ✅ serf.KeyManager.*
- ✅ serf.SetTagsAsync()
- ✅ serf.IpcEventReader

**Tests:** 69 tests

---

### **Phase 7: Event Handlers** (Week 8)
**Focus:** Script execution for events

**Scope:**
- Implement ScriptEventHandler
- Add ScriptInvoker with Process management
- Implement event filtering
- Add platform-specific shell invocation
- Handle environment variables
- Add stdin/stdout handling

**Files to Create:**
- `Agent/ScriptEventHandler.cs`
- `Agent/ScriptInvoker.cs`
- `Agent/EventScript.cs`
- `Agent/EventFilter.cs`

**Uses:**
- ✅ agent.RegisterEventHandler()
- ✅ Event system (from serf.IpcEventReader)

**Tests:** 35 tests

---

### **Phase 8: CLI Integration** (Weeks 9-10)
**Focus:** Complete command-line agent

**Scope:**
- Implement Command.cs (main CLI)
- Add signal handling (SIGTERM/SIGINT/SIGHUP)
- Implement config reload
- Add log management (LogWriter, GatedWriter)
- Add mDNS discovery
- Implement graceful shutdown

**Files to Create:**
- `Agent/Command.cs` (~400 lines)
- `Agent/LogWriter.cs` (~100 lines)
- `Agent/GatedWriter.cs` (~50 lines)
- `Agent/AgentMdns.cs` (~150 lines)
- `Agent/Util.cs` (~50 lines)

**Uses:**
- ✅ Agent (from Phase 5)
- ✅ AgentIpc (from Phase 6)
- ✅ ScriptEventHandler (from Phase 7)

**Tests:** 52 tests

---

## 🎯 Recommended Implementation Order

### **Option 1: Client-First (Recommended)**

**Advantages:**
- Can test against Go agent immediately
- Validates protocol understanding early
- Independent of Agent implementation
- Faster feedback loop

**Order:**
1. Phase 1: RPC Client Foundation (1 week)
2. Phase 2: RPC Commands (1 week)
3. Phase 3: Streaming Operations (1 week)
4. *Test extensively against Go agent*
5. Phase 4: Agent Configuration (1 week)
6. Phase 5: Agent Core (1 week)
7. Phase 6: IPC Server (2 weeks)
8. Phase 7: Event Handlers (1 week)
9. Phase 8: CLI Integration (2 weeks)

**Timeline:** 10 weeks

---

### **Option 2: Agent-First**

**Advantages:**
- Complete server before client
- Can use Go RPC client for testing

**Order:**
1. Phase 4: Agent Configuration (1 week)
2. Phase 5: Agent Core (1 week)
3. Phase 6: IPC Server (2 weeks)
4. *Test with Go RPC client*
5. Phase 7: Event Handlers (1 week)
6. Phase 8: CLI Integration (2 weeks)
7. Phase 1: RPC Client Foundation (1 week)
8. Phase 2: RPC Commands (1 week)
9. Phase 3: Streaming Operations (1 week)

**Timeline:** 10 weeks

---

### **Option 3: Parallel (Advanced)**

**Advantages:**
- Fastest completion
- Two developers can work independently

**Split:**
- **Developer A:** Phases 1-3 (Client) - 3 weeks
- **Developer B:** Phases 4-5 (Config + Agent) - 2 weeks, then Phase 6 (IPC) - 2 weeks

Then both work on Phases 7-8.

**Timeline:** 8 weeks with 2 developers

---

## ✅ Verification Checkpoints

### **After Phase 1:**
- [ ] RpcClient connects to Go agent
- [ ] Handshake succeeds
- [ ] Auth works
- [ ] MessagePack encoding correct

### **After Phase 3:**
- [ ] All commands work against Go agent
- [ ] Streaming operations work
- [ ] Error handling correct

### **After Phase 5:**
- [ ] Agent creates Serf instance
- [ ] Join/Leave work
- [ ] Tags persist
- [ ] Keyring persists

### **After Phase 6:**
- [ ] RPC client connects to our Agent
- [ ] All commands work
- [ ] Streaming works
- [ ] Query responses correct

### **After Phase 8:**
- [ ] CLI agent starts/stops
- [ ] Config reload works
- [ ] Signal handling correct
- [ ] Full integration tests pass

---

## 📊 Test Coverage Summary

| Layer | Tests | Coverage |
|-------|-------|----------|
| **Serf Library** | Existing | ✅ Complete |
| **RPC Client** | 74 | Phases 1-3 |
| **Agent Config** | 41 | Phase 4 |
| **Agent Core** | 38 | Phase 5 |
| **IPC Server** | 69 | Phase 6 |
| **Event Scripts** | 35 | Phase 7 |
| **CLI Integration** | 52 | Phase 8 |
| **TOTAL** | **309** | **Agent/Client** |

---

## 🚀 Next Steps

### **Immediate Actions:**

1. **Review Documentation** ✅
   - [x] PHASE0_FOUNDATION_STATUS.md
   - [x] ARCHITECTURE_ANALYSIS.md
   - [x] IMPLEMENTATION_ROADMAP.md (this document)

2. **Prepare Development Environment**
   - [ ] Set up test project structure
   - [ ] Install Go serf for testing
   - [ ] Configure test infrastructure

3. **Begin Phase 1**
   - [ ] Create Client/ directory
   - [ ] Implement RpcClient.cs skeleton
   - [ ] Add TCP connection logic
   - [ ] Write first test

### **Weekly Milestones:**

- **Week 1:** RPC Client Foundation complete
- **Week 2:** All RPC commands working
- **Week 3:** Streaming operations complete
- **Week 4:** Agent configuration system ready
- **Week 5:** Agent wrapper functional
- **Week 6-7:** IPC server complete
- **Week 8:** Event scripts working
- **Week 9-10:** CLI integration and final testing

---

## 🎉 Summary

**Foundation Status:** ✅ SOLID

- Memberlist library: Complete
- Serf library core: Complete
- State machine: Verified correct
- Managers: Transaction API ready
- Snapshotter: Recovery working
- All APIs: Available for Agent

**Remaining Work:** Agent & Client layers only

- Client package: Phases 1-3 (3 weeks)
- Agent package: Phases 4-8 (7 weeks)
- Total: 10 weeks, 309 tests

**Key Insight:** We're not porting Serf, we're building a command-line agent wrapper around an already-complete Serf library!

**Next:** Start Phase 1 - Build RPC Client! 🚀

---

## 📚 Reference Documents

1. **PHASE0_FOUNDATION_STATUS.md** - What exists vs. what's needed
2. **ARCHITECTURE_ANALYSIS.md** - Detailed compatibility analysis
3. **PHASES_OVERVIEW.md** - Updated with scope clarification
4. **PHASE1-8 Files** - Detailed test specifications (309 tests)
5. **Verification Reports** - Go compatibility verification

**All documentation is consistent and ready for implementation!**
