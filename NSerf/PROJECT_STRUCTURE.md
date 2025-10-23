# Agent Port Project Structure

This document shows the complete directory structure to be created for the Serf Agent port.

## Directory Structure to Create

```
NSerf/
├── NSerf/
│   ├── Client/                              # Phase 1-3: RPC Client Library
│   │   ├── RpcClient.cs                     # Main RPC client (~400 lines)
│   │   ├── RpcConfig.cs                     # Client configuration (~50 lines)
│   │   ├── RpcProtocol.cs                   # Protocol constants & types (~100 lines)
│   │   ├── StreamHandle.cs                  # Stream handle type (~20 lines)
│   │   ├── SeqHandler.cs                    # Sequence handler interface (~50 lines)
│   │   ├── SeqCallback.cs                   # Simple callback handler (~30 lines)
│   │   ├── Handlers/                        # Phase 3: Streaming handlers
│   │   │   ├── MonitorHandler.cs           # Log monitor handler (~120 lines)
│   │   │   ├── StreamHandler.cs            # Event stream handler (~120 lines)
│   │   │   └── QueryHandler.cs             # Query handler (~100 lines)
│   │   ├── Requests/                        # Phase 2: Request DTOs
│   │   │   ├── HandshakeRequest.cs
│   │   │   ├── AuthRequest.cs
│   │   │   ├── JoinRequest.cs
│   │   │   ├── MembersFilteredRequest.cs
│   │   │   ├── EventRequest.cs
│   │   │   ├── ForceLeaveRequest.cs
│   │   │   ├── KeyRequest.cs
│   │   │   ├── QueryRequest.cs
│   │   │   ├── TagsRequest.cs
│   │   │   ├── StreamRequest.cs
│   │   │   ├── MonitorRequest.cs
│   │   │   ├── StopRequest.cs
│   │   │   ├── RespondRequest.cs
│   │   │   └── CoordinateRequest.cs
│   │   ├── Responses/                       # Phase 2: Response DTOs
│   │   │   ├── JoinResponse.cs
│   │   │   ├── MembersResponse.cs
│   │   │   ├── KeyResponse.cs
│   │   │   ├── CoordinateResponse.cs
│   │   │   ├── LogRecord.cs
│   │   │   ├── QueryRecord.cs
│   │   │   └── NodeResponse.cs
│   │   └── Exceptions/                      # Phase 1: Custom exceptions
│   │       ├── RpcException.cs
│   │       ├── RpcAuthException.cs
│   │       ├── RpcTimeoutException.cs
│   │       └── RpcProtocolException.cs
│   │
│   ├── Agent/                              # Phase 4-8: Agent Implementation
│   │   ├── Agent.cs                        # Phase 5: Main agent (~500 lines)
│   │   ├── AgentConfig.cs                  # Phase 4: Configuration (~300 lines)
│   │   ├── AgentState.cs                   # Phase 5: State enum (~20 lines)
│   │   ├── SerfConfig.cs                   # Phase 4: Serf config (~150 lines)
│   │   ├── ConfigValidator.cs              # Phase 4: Validation (~200 lines)
│   │   ├── ConfigLoader.cs                 # Phase 4: JSON loading (~150 lines)
│   │   ├── IEventHandler.cs                # Phase 5: Handler interface (~20 lines)
│   │   ├── EventScript.cs                  # Phase 4: Script definition (~100 lines)
│   │   ├── EventFilter.cs                  # Phase 4: Event filter (~80 lines)
│   │   ├── ScriptEventHandler.cs           # Phase 7: Script handler (~150 lines)
│   │   ├── ScriptInvoker.cs                # Phase 7: Script execution (~200 lines)
│   │   ├── AgentIpc.cs                     # Phase 6: IPC server (~600 lines)
│   │   ├── IpcClient.cs                    # Phase 6: Client wrapper (~200 lines)
│   │   ├── IpcEventStream.cs               # Phase 6: Event streaming (~150 lines)
│   │   ├── IpcLogStream.cs                 # Phase 6: Log streaming (~80 lines)
│   │   ├── IpcQueryResponseStream.cs       # Phase 6: Query streaming (~100 lines)
│   │   ├── Command.cs                      # Phase 8: CLI command (~400 lines)
│   │   ├── LogWriter.cs                    # Phase 8: Log writer (~100 lines)
│   │   ├── GatedWriter.cs                  # Phase 8: Gated writer (~50 lines)
│   │   ├── LogLevels.cs                    # Phase 8: Log level filter (~50 lines)
│   │   ├── AgentMdns.cs                    # Phase 8: mDNS discovery (~150 lines)
│   │   ├── Util.cs                         # Phase 8: Utilities (~50 lines)
│   │   └── Exceptions/                     # Phase 5: Agent exceptions
│   │       ├── AgentException.cs
│   │       └── AgentStateException.cs
│   │
│   └── NSerf.csproj                        # Update with new packages
│
├── NSerfTests/
│   ├── Client/                             # Phase 1-3: RPC Client Tests
│   │   ├── RpcClientTests.cs               # Phase 1: Connection/handshake (5 tests)
│   │   ├── RpcAuthTests.cs                 # Phase 1: Authentication (3 tests)
│   │   ├── RpcProtocolTests.cs             # Phase 1: Protocol (7 tests)
│   │   ├── RpcMembershipTests.cs           # Phase 2: Membership (8 tests)
│   │   ├── RpcEventTests.cs                # Phase 2: Events (4 tests)
│   │   ├── RpcKeyTests.cs                  # Phase 2: Key management (8 tests)
│   │   ├── RpcQueryTests.cs                # Phase 2: Queries (5 tests)
│   │   ├── RpcMiscTests.cs                 # Phase 2: Other commands (4 tests)
│   │   ├── RpcMonitorTests.cs              # Phase 3: Monitor (6 tests)
│   │   ├── RpcStreamTests.cs               # Phase 3: Streaming (8 tests)
│   │   ├── RpcStopTests.cs                 # Phase 3: Stop (3 tests)
│   │   └── Helpers/
│   │       ├── MockRpcServer.cs            # Mock server for testing
│   │       ├── TestRpcFactory.cs           # Test factory methods
│   │       ├── NonResponsiveMockServer.cs  # Timeout testing
│   │       └── SlowMockServer.cs           # Slow I/O testing
│   │
│   ├── Agent/                              # Phase 4-8: Agent Tests
│   │   ├── AgentConfigTests.cs             # Phase 4: Config (3 tests)
│   │   ├── ConfigLoadTests.cs              # Phase 4: Loading (8 tests)
│   │   ├── ConfigValidationTests.cs        # Phase 4: Validation (8 tests)
│   │   ├── TagsPersistenceTests.cs         # Phase 4: Tags (5 tests)
│   │   ├── EventScriptTests.cs             # Phase 4: Scripts (5 tests)
│   │   ├── KeyringTests.cs                 # Phase 4: Keyring (4 tests)
│   │   ├── AgentLifecycleTests.cs          # Phase 5: Lifecycle (8 tests)
│   │   ├── AgentEventHandlerTests.cs       # Phase 5: Handlers (6 tests)
│   │   ├── AgentOperationsTests.cs         # Phase 5: Operations (8 tests)
│   │   ├── AgentTagsTests.cs               # Phase 5: Tags mgmt (5 tests)
│   │   ├── AgentKeyringTests.cs            # Phase 5: Keyring mgmt (4 tests)
│   │   ├── IpcServerTests.cs               # Phase 6: IPC lifecycle (6 tests)
│   │   ├── IpcHandshakeTests.cs            # Phase 6: Handshake (5 tests)
│   │   ├── IpcAuthTests.cs                 # Phase 6: Auth (4 tests)
│   │   ├── IpcMembershipTests.cs           # Phase 6: Membership (8 tests)
│   │   ├── IpcJoinLeaveTests.cs            # Phase 6: Join/Leave (6 tests)
│   │   ├── IpcEventTests.cs                # Phase 6: Events (5 tests)
│   │   ├── IpcKeyTests.cs                  # Phase 6: Keys (8 tests)
│   │   ├── IpcQueryTests.cs                # Phase 6: Queries (8 tests)
│   │   ├── IpcStreamTests.cs               # Phase 6: Streaming (6 tests)
│   │   ├── IpcStopTests.cs                 # Phase 6: Stop (2 tests)
│   │   ├── IpcMiscTests.cs                 # Phase 6: Other (2 tests)
│   │   ├── EventFilterTests.cs             # Phase 7: Filters (5 tests)
│   │   ├── EventFilterMatchTests.cs        # Phase 7: Matching (6 tests)
│   │   ├── ScriptExecutionTests.cs         # Phase 7: Execution (8 tests)
│   │   ├── ScriptEnvironmentTests.cs       # Phase 7: Env vars (4 tests)
│   │   ├── QueryResponseTests.cs           # Phase 7: Query (2 tests)
│   │   ├── CommandTests.cs                 # Phase 8: CLI (8 tests)
│   │   ├── LogManagementTests.cs           # Phase 8: Logging (5 tests)
│   │   ├── SignalHandlingTests.cs          # Phase 8: Signals (4 tests)
│   │   ├── ConfigReloadTests.cs            # Phase 8: Reload (6 tests)
│   │   ├── MdnsTests.cs                    # Phase 8: mDNS (5 tests)
│   │   ├── IntegrationTests.cs             # Phase 8: Integration (12 tests)
│   │   └── Helpers/
│   │       ├── MockEventHandler.cs         # Mock event handler
│   │       ├── TestAgentFactory.cs         # Agent factory
│   │       ├── TestConfigFactory.cs        # Config factory
│   │       ├── TempFileHelper.cs           # Temp file management
│   │       └── ProcessHelper.cs            # Process testing utilities
│   │
│   └── NSerfTests.csproj                   # Update with test packages
│
├── AGENT_PORT_TDD_CHECKLIST.md            # ✅ High-level roadmap
├── AGENT_PORT_SUMMARY.md                   # ✅ Getting started guide
├── PHASE1_RPC_CLIENT_TESTS.md              # ✅ Detailed Phase 1 spec
└── PROJECT_STRUCTURE.md                    # ✅ This file
```

## NuGet Packages to Add

### NSerf.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Existing packages -->
    <!-- ... -->

    <!-- New packages for Agent port -->
    <PackageReference Include="MessagePack" Version="2.5.140" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
    <PackageReference Include="System.Threading.Channels" Version="8.0.0" />
    <PackageReference Include="System.Text.Json" Version="8.0.0" />
  </ItemGroup>
</Project>
```

### NSerfTests.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Existing packages -->
    <PackageReference Include="xunit" Version="2.6.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
    
    <!-- New packages for Agent tests -->
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\NSerf\NSerf.csproj" />
  </ItemGroup>
</Project>
```

## File Sizes Reference

| Component | Files | Lines | Tests | Test Lines |
|-----------|-------|-------|-------|------------|
| RPC Client (P1-3) | 15 | ~1,600 | 61 | ~2,400 |
| Configuration (P4) | 6 | ~980 | 33 | ~1,200 |
| Agent Core (P5) | 5 | ~600 | 31 | ~1,200 |
| IPC Server (P6) | 5 | ~1,130 | 60 | ~2,400 |
| Event Handlers (P7) | 3 | ~500 | 25 | ~1,000 |
| CLI & Integration (P8) | 6 | ~800 | 40 | ~1,800 |
| **Total** | **40** | **~5,610** | **250** | **~10,000** |

## Setup Commands

```bash
# Navigate to project root
cd /c/Users/bilel/Desktop/SerfPort/NSerf

# Create directory structure
mkdir -p NSerf/Client/Handlers
mkdir -p NSerf/Client/Requests
mkdir -p NSerf/Client/Responses
mkdir -p NSerf/Client/Exceptions
mkdir -p NSerf/Agent/Exceptions

mkdir -p NSerfTests/Client/Helpers
mkdir -p NSerfTests/Agent/Helpers

# Add NuGet packages
cd NSerf
dotnet add package MessagePack --version 2.5.140
dotnet add package Microsoft.Extensions.Logging.Abstractions --version 8.0.0
dotnet add package System.Threading.Channels --version 8.0.0
dotnet add package System.Text.Json --version 8.0.0

cd ../NSerfTests
dotnet add package Moq --version 4.20.70
dotnet add package FluentAssertions --version 6.12.0

# Build to verify
cd ..
dotnet build
```

## Phase-by-Phase File Creation

### Phase 1 (Week 1)
```
✅ Create:
- NSerf/Client/RpcClient.cs
- NSerf/Client/RpcConfig.cs
- NSerf/Client/RpcProtocol.cs
- NSerf/Client/SeqHandler.cs
- NSerf/Client/SeqCallback.cs
- NSerf/Client/Exceptions/*.cs
- NSerfTests/Client/RpcClientTests.cs
- NSerfTests/Client/RpcAuthTests.cs
- NSerfTests/Client/RpcProtocolTests.cs
- NSerfTests/Client/Helpers/MockRpcServer.cs
```

### Phase 2 (Week 2)
```
✅ Update: NSerf/Client/RpcClient.cs
✅ Create:
- NSerf/Client/Requests/*.cs (14 files)
- NSerf/Client/Responses/*.cs (7 files)
- NSerfTests/Client/RpcMembershipTests.cs
- NSerfTests/Client/RpcEventTests.cs
- NSerfTests/Client/RpcKeyTests.cs
- NSerfTests/Client/RpcQueryTests.cs
- NSerfTests/Client/RpcMiscTests.cs
```

### Phase 3 (Week 3)
```
✅ Create:
- NSerf/Client/StreamHandle.cs
- NSerf/Client/Handlers/*.cs (3 files)
- NSerfTests/Client/RpcMonitorTests.cs
- NSerfTests/Client/RpcStreamTests.cs
- NSerfTests/Client/RpcStopTests.cs
```

### Phase 4 (Week 4)
```
✅ Create:
- NSerf/Agent/AgentConfig.cs
- NSerf/Agent/SerfConfig.cs
- NSerf/Agent/EventScript.cs
- NSerf/Agent/EventFilter.cs
- NSerf/Agent/ConfigValidator.cs
- NSerf/Agent/ConfigLoader.cs
- NSerfTests/Agent/AgentConfigTests.cs
- NSerfTests/Agent/ConfigLoadTests.cs
- NSerfTests/Agent/ConfigValidationTests.cs
- NSerfTests/Agent/TagsPersistenceTests.cs
- NSerfTests/Agent/EventScriptTests.cs
- NSerfTests/Agent/KeyringTests.cs
- NSerfTests/Agent/Helpers/TestConfigFactory.cs
- NSerfTests/Agent/Helpers/TempFileHelper.cs
```

### Phase 5 (Week 5)
```
✅ Create:
- NSerf/Agent/Agent.cs
- NSerf/Agent/AgentState.cs
- NSerf/Agent/IEventHandler.cs
- NSerf/Agent/Exceptions/*.cs (2 files)
- NSerfTests/Agent/AgentLifecycleTests.cs
- NSerfTests/Agent/AgentEventHandlerTests.cs
- NSerfTests/Agent/AgentOperationsTests.cs
- NSerfTests/Agent/AgentTagsTests.cs
- NSerfTests/Agent/AgentKeyringTests.cs
- NSerfTests/Agent/Helpers/MockEventHandler.cs
- NSerfTests/Agent/Helpers/TestAgentFactory.cs
```

### Phase 6 (Week 6-7)
```
✅ Create:
- NSerf/Agent/AgentIpc.cs
- NSerf/Agent/IpcClient.cs
- NSerf/Agent/IpcEventStream.cs
- NSerf/Agent/IpcLogStream.cs
- NSerf/Agent/IpcQueryResponseStream.cs
- NSerfTests/Agent/IpcServerTests.cs (11 test files)
```

### Phase 7 (Week 8)
```
✅ Create:
- NSerf/Agent/ScriptEventHandler.cs
- NSerf/Agent/ScriptInvoker.cs
- NSerfTests/Agent/EventFilterTests.cs
- NSerfTests/Agent/EventFilterMatchTests.cs
- NSerfTests/Agent/ScriptExecutionTests.cs
- NSerfTests/Agent/ScriptEnvironmentTests.cs
- NSerfTests/Agent/QueryResponseTests.cs
- NSerfTests/Agent/Helpers/ProcessHelper.cs
```

### Phase 8 (Week 9-10)
```
✅ Create:
- NSerf/Agent/Command.cs
- NSerf/Agent/LogWriter.cs
- NSerf/Agent/GatedWriter.cs
- NSerf/Agent/LogLevels.cs
- NSerf/Agent/AgentMdns.cs
- NSerf/Agent/Util.cs
- NSerfTests/Agent/CommandTests.cs (6 test files + Integration)
```

## Git Workflow

```bash
# Create feature branch for agent port
git checkout -b feature/agent-port

# After each phase, commit
git add .
git commit -m "Phase 1: RPC Client Foundation - 15 tests passing"

# Push to remote
git push origin feature/agent-port

# When all phases complete, merge to main
git checkout main
git merge feature/agent-port
```

## Verification Checklist

After creating structure:
- [ ] All directories exist
- [ ] NuGet packages restored successfully
- [ ] Solution builds without errors
- [ ] Test project references main project
- [ ] Can run empty test suite
- [ ] IDE recognizes new folders
- [ ] Git tracking new files

## Ready to Start!

Structure is ready. Begin with **Phase 1, Test 1.1.1**:
```bash
# Create first test file
cd NSerfTests/Client
# Open RpcClientTests.cs and copy first test from PHASE1_RPC_CLIENT_TESTS.md
```

🎯 **Goal:** RED → GREEN → REFACTOR → REPEAT!
