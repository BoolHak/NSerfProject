# NSerf IPC Server & Client - TDD Implementation Plan

## Overview
**Goal:** Build production-ready IPC/RPC for NSerf (TCP + MessagePack)  
**Method:** 🔴 RED → 🟢 GREEN → 🔵 REFACTOR  
**Timeline:** 8 weeks, 16 phases  
**Total:** ~7,000 lines (3,600 tests + 3,400 code)

## Quick Reference

**18 Commands:** handshake, auth, join, leave, force-leave, members, members-filtered, event, tags, stream, monitor, stop, install-key, use-key, remove-key, list-keys, query, respond, stats, get-coordinate

**10 Errors:** Unsupported command/version, Handshake already performed/required, Monitor/Stream exists, Invalid filter/query ID, Auth required/invalid token

**4 Handler Types:** SimpleResponseHandler, MonitorHandler, StreamHandler, QueryHandler

---

## Phase 1-2: Protocol & Models (Week 1)

### Phase 1: Protocol Constants

**🔴 Tests (100 lines):** Protocol validation - versions, commands, errors uniqueness  
**🟢 Code (100 lines):** `IpcProtocol.cs` - All constants  
**🔵 Refactor:** XML docs

### Phase 2: Core Models

**🔴 Tests (400 lines):** Serialize/deserialize all 25+ models  
**🟢 Code (600 lines):** `IpcModels.cs` - All request/response types

**Critical models:**
- RequestHeader, ResponseHeader
- IpcMember (11 fields: Name, Addr, Port, Tags, Status, 6x Protocol versions)
- UserEventRecord, QueryEventRecord, MemberEventRecord
- KeyResponse (5 fields including Keys dict)

---

## Phase 3-5: Server Core (Week 2)

### Phase 3: IpcClientHandler

**🔴 Tests (200 lines):**
- SendAsync serialization + concurrency
- Query registration with auto-deregistration
- Version/auth state tracking

**🟢 Code (250 lines):** `IpcClientHandler.cs`
```csharp
- SendAsync(header, body) with write lock
- RegisterQuery() with atomic increment
- Version, DidAuth properties
- LogStreamer, EventStreams dictionaries
```

### Phase 4: AgentIpc Server

**🔴 Tests (300 lines):**
- Accept connections
- Handshake enforcement
- Auth enforcement (when configured)
- Concurrent clients (10x)
- Shutdown cleanup

**🟢 Code (400 lines):** `AgentIpc.cs`
```csharp
- TCP listener on RPC address
- Client connection management
- HandleClientAsync loop
- HandleRequestAsync routing
- Command dispatcher (switch on command name)
```

### Phase 5: Handshake & Auth Handlers

**🔴 Tests (250 lines):**
- Valid/invalid version
- Duplicate handshake
- Commands blocked pre-handshake
- Valid/invalid auth key
- Commands blocked pre-auth

**🟢 Code (80 lines):** `AgentIpc.Commands.cs`
```csharp
- HandleHandshakeAsync(version validation)
- HandleAuthAsync(key comparison)
```

---

## Phase 6-8: Command Handlers (Week 3)

### Phase 6: Member Commands

**🔴 Tests (400 lines):**
- Members returns all + converts types
- MembersFiltered: tags(regex), status, name, combined
- Regex anchoring (^pattern$)

**🟢 Code (250 lines):**
```csharp
- HandleMembersAsync
- HandleMembersFilteredAsync
- FilterMembers(tags, status, name) - regex with ^ and $
```

### Phase 7: Cluster Commands

**🔴 Tests (200 lines):**
- Join returns count
- Leave, ForceLeave (prune flag)

**🟢 Code (100 lines):**
```csharp
- HandleJoinAsync
- HandleLeaveAsync
- HandleForceLeaveAsync
```

### Phase 8: Event & Stats

**🔴 Tests (200 lines):**
- UserEvent, UpdateTags, Stats, GetCoordinate

**🟢 Code (150 lines):**
```csharp
- HandleEventAsync
- HandleTagsAsync(merge + delete)
- HandleStatsAsync
- HandleGetCoordinateAsync
```

---

## Phase 9: Key Management (Week 3)

**🔴 Tests (200 lines):** InstallKey, UseKey, RemoveKey, ListKeys  
**🟢 Code (150 lines):** `AgentIpc.KeyManagement.cs`

---

## Phase 10-12: Streaming (Week 4-5)

### Phase 10: Log Streaming

**🔴 Tests (300 lines):**
- Monitor streams logs
- Log level filtering
- Duplicate rejection
- Stop command
- Multiple clients
- Disconnect cleanup

**🟢 Code (250 lines):**
```csharp
// Streaming/LogStream.cs
- Channel<string> for logs
- Filter by log level
- Non-blocking TryWrite

// AgentIpc.Streaming.cs
- HandleMonitorAsync
- HandleStopAsync
```

### Phase 11: Event Streaming

**🔴 Tests (350 lines):**
- Filter by type (member-join, user, query)
- Wildcards
- Multiple streams/filters

**🟢 Code (350 lines):**
```csharp
// Streaming/EventFilter.cs - Parse filters
// Streaming/EventStream.cs - Capture events
- Non-blocking channel writes
- Convert Serf events to IPC records
```

### Phase 12: Query System

**🔴 Tests (400 lines):**
- Filter nodes/tags
- Stream acks + responses
- Done signal
- Respond command
- Invalid query ID error
- Timeout auto-deregister

**🟢 Code (290 lines):**
```csharp
// Streaming/QueryResponseStream.cs
- Handle ack/response/done records
- Two channels: acks, responses

// AgentIpc.Query.cs
- HandleQueryAsync
- HandleRespondAsync
```

---

## Phase 13-14: Client Library (Week 6-7)

### Phase 13: Client Core

**🔴 Tests (300 lines):**
- Connect + handshake
- Sequence generation (atomic)
- Handler dispatch by sequence
- Concurrent requests
- Timeout handling

**🟢 Code (400 lines):** `SerfRpcClient.cs`
```csharp
- ConcurrentDictionary<ulong, IResponseHandler> handlers
- SemaphoreSlim writeLock (single writer)
- GetNextSequence() with Interlocked.Increment
- ListenAsync() background task
- SendAsync(header, body)
```

### Phase 14: Response Handlers

**🔴 Tests (200 lines):**
- Handler lifecycle (init → handle → cleanup)
- Non-blocking channel writes
- Initialization error handling

**🟢 Code (300 lines):**
```csharp
// IResponseHandler interface
public interface IResponseHandler
{
    Task HandleAsync(ResponseHeader header);
    Task CleanupAsync();
}

// SimpleResponseHandler - one-off RPCs
- TaskCompletionSource<T>
- Cleanup: no-op

// MonitorHandler - log streaming
- First call: initialization check
- Subsequent: decode LogRecord, TryWrite to channel
- Cleanup: close channel

// StreamHandler - event streaming
- Decode event records (map<string,object>)
- TryWrite (non-blocking)
- Cleanup: close channel

// QueryHandler - query responses
- Decode QueryRecord
- Switch on Type: ack → ackChannel, response → respChannel, done → cleanup
- Cleanup: close both channels
```

---

## Phase 15: Client Commands (Week 7)

**🔴 Tests (300 lines):** All 18 commands work  
**🟢 Code (500 lines):** All client methods

```csharp
public class SerfRpcClient
{
    // Simple RPCs
    Task<int> JoinAsync(string[] addrs, bool replay, CancellationToken ct);
    Task LeaveAsync(CancellationToken ct);
    Task ForceLeaveAsync(string node, bool prune, CancellationToken ct);
    Task<IpcMember[]> GetMembersAsync(CancellationToken ct);
    Task<IpcMember[]> GetMembersFilteredAsync(tags, status, name, CancellationToken ct);
    Task UserEventAsync(string name, byte[] payload, bool coalesce, CancellationToken ct);
    Task UpdateTagsAsync(tags, deleteTags, CancellationToken ct);
    
    // Key management
    Task<KeyResponse> InstallKeyAsync(string key, CancellationToken ct);
    Task<KeyResponse> UseKeyAsync(string key, CancellationToken ct);
    Task<KeyResponse> RemoveKeyAsync(string key, CancellationToken ct);
    Task<KeyResponse> ListKeysAsync(CancellationToken ct);
    
    // Info
    Task<Dictionary<string, Dictionary<string, string>>> GetStatsAsync(CancellationToken ct);
    Task<Coordinate?> GetCoordinateAsync(string node, CancellationToken ct);
    
    // Streaming - Returns ChannelReader<T>
    Task<ChannelReader<string>> MonitorAsync(LogLevel level, CancellationToken ct);
    Task<ChannelReader<Dictionary<string, object>>> StreamAsync(string filter, CancellationToken ct);
    Task<(ChannelReader<string> acks, ChannelReader<NodeResponse> responses)> 
        QueryAsync(QueryParam param, CancellationToken ct);
    
    // Two-phase stop: deregister locally, then send stop command
    Task StopAsync(StreamHandle handle, CancellationToken ct);
}
```

**StreamHandle:**
```csharp
public readonly struct StreamHandle
{
    private readonly ulong _seq;
    public StreamHandle(ulong seq) => _seq = seq;
    public static implicit operator ulong(StreamHandle h) => h._seq;
}
```

---

## Phase 16: Integration (Week 8)

### Integration Tests (600 lines)

**Tests:**
- Real Serf agent with RPC enabled
- Multi-node cluster operations
- End-to-end all commands
- Streaming with real events
- Stress test (100 concurrent clients)

### Serf Integration (50 lines)

**Add to SerfConfig:**
```csharp
public string? RpcAddr { get; set; }
public string? RpcAuthKey { get; set; }
public bool RpcEnabled { get; set; }
```

**Add to Serf.CreateAsync:**
```csharp
if (config.RpcEnabled && !string.IsNullOrEmpty(config.RpcAddr))
{
    serf._ipcServer = new AgentIpc(serf, config.RpcAddr, config.RpcAuthKey);
    await serf._ipcServer.StartAsync(cancellationToken);
}
```

---

## Critical Implementation Patterns

### 1. Non-Blocking Channel Writes (ALL handlers)
```csharp
// NEVER do: await channel.Writer.WriteAsync(data)
// ALWAYS do:
if (!channel.Writer.TryWrite(data))
{
    _logger?.LogWarning("Dropping data, channel full");
}
```

### 2. Two-Phase Stop
```csharp
public async Task StopAsync(StreamHandle handle)
{
    // Phase 1: Stop receiving locally
    _handlers.TryRemove(handle, out var handler);
    if (handler != null) 
        await handler.CleanupAsync();
    
    // Phase 2: Tell server to stop sending
    await SendStopCommandAsync(handle);
}
```

### 3. Handler Initialization Pattern
```csharp
public async Task HandleAsync(ResponseHeader header)
{
    if (!_initialized)
    {
        _initialized = true;
        if (!string.IsNullOrEmpty(header.Error))
            _initTcs.SetException(new RpcException(header.Error));
        else
            _initTcs.SetResult(true);
        return;
    }
    
    // Decode and handle data...
}
```

### 4. Regex Anchoring for Filters
```csharp
private Regex CompileFilter(string pattern)
{
    return new Regex($"^{pattern}$", RegexOptions.Compiled);
}
```

---

## File Structure

```
NSerf/NSerf/Client/
├── IpcProtocol.cs                (100)
├── IpcModels.cs                  (600)
├── IpcClientHandler.cs           (250)
├── AgentIpc.cs                   (400)
├── AgentIpc.Commands.cs          (450)
├── AgentIpc.KeyManagement.cs     (150)
├── AgentIpc.Streaming.cs         (200)
├── AgentIpc.Query.cs             (140)
├── Streaming/
│   ├── LogStream.cs              (150)
│   ├── EventFilter.cs            (100)
│   ├── EventStream.cs            (150)
│   └── QueryResponseStream.cs    (150)
├── SerfRpcClient.cs              (400)
├── ResponseHandlers.cs           (300)
└── StreamHandle.cs               (20)

NSerfTests/Client/
├── IpcProtocolTests.cs           (100)
├── IpcModelsTests.cs             (400)
├── IpcClientHandlerTests.cs      (200)
├── AgentIpcServerTests.cs        (300)
├── HandshakeAuthTests.cs         (250)
├── MemberCommandTests.cs         (400)
├── ClusterCommandTests.cs        (200)
├── EventStatsTests.cs            (200)
├── KeyManagementTests.cs         (200)
├── LogStreamingTests.cs          (300)
├── EventStreamingTests.cs        (350)
├── QueryTests.cs                 (400)
├── ClientCoreTests.cs            (300)
├── ResponseHandlerTests.cs       (200)
├── ClientCommandTests.cs         (300)
└── IntegrationTests.cs           (600)
```

**Total:**  
- Server: 2,620 lines  
- Client: 820 lines  
- Tests: 3,600 lines  
- **Grand Total: 7,040 lines**

---

## Timeline Summary

| Week | Phases | Focus | Lines |
|------|--------|-------|-------|
| 1 | 1-2 | Protocol + Models | 1,200 |
| 2 | 3-5 | Server Core + Auth | 1,230 |
| 3 | 6-9 | Commands + Keys | 900 |
| 4-5 | 10-12 | Streaming | 1,390 |
| 6-7 | 13-15 | Client Library | 1,500 |
| 8 | 16 | Integration | 650 |

---

## Success Criteria

- [ ] All tests pass (>90% coverage)
- [ ] Zero new dependencies
- [ ] Thread-safe operations
- [ ] Protocol-compatible with Go serf
- [ ] Handles 100+ concurrent clients
- [ ] Non-blocking streaming
- [ ] Graceful shutdown
- [ ] Comprehensive docs

Ready to start Phase 1! 🚀
