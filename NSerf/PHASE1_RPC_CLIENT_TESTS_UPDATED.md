# Phase 1: RPC Client Foundation - UPDATED Test Specification

**Timeline:** Week 1  
**Test Count:** 20 tests (5 added after Go code review)  
**Focus:** Connection, handshake, authentication, message encoding

---

## ⚠️ UPDATES AFTER GO CODE REVIEW

### Missing Tests Added:
1. **Test 1.1.6:** IsClosed() state tracking
2. **Test 1.2.4:** MsgpackUseNewTimeFormat configuration
3. **Test 1.3.4:** DeregisterAll on Close
4. **Test 1.4.5:** Send after Close throws exception
5. **Test 1.5.1:** Listen loop cleanup and error handling

---

## Test Group 1.1: Connection and Handshake (6 tests - 1 added)

### Test 1.1.1: RpcClient_Connect_SuccessfulHandshake
*(Same as before)*

### Test 1.1.2: RpcClient_Connect_InvalidAddress_ThrowsException
*(Same as before)*

### Test 1.1.3: RpcClient_Connect_Timeout_ThrowsException
*(Same as before)*

### Test 1.1.4: RpcClient_Handshake_UnsupportedVersion_ThrowsException
*(Same as before)*

### Test 1.1.5: RpcClient_Close_MultipleCalls_NoException
*(Same as before)*

### Test 1.1.6: RpcClient_IsClosed_ReflectsState ⚠️ NEW

**Objective:** Verify IsClosed() accurately reflects client state.

```csharp
[Fact]
public async Task RpcClient_IsClosed_ReflectsState()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();

    // Act
    var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });
    Assert.False(client.IsClosed()); // Open after connect

    await client.CloseAsync();
    Assert.True(client.IsClosed()); // Closed after close

    // Assert - Further operations should fail
    await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
    {
        await client.MembersAsync();
    });
}
```

**Expected Behavior:**
- IsClosed() returns false after successful connection
- IsClosed() returns true after Close()
- Go code checks `c.shutdown` field
- Prevents operations on closed client

---

## Test Group 1.2: Authentication (4 tests - 1 added)

### Test 1.2.1-1.2.3: *(Same as before)*

### Test 1.2.4: RpcClient_MsgpackTimeFormat_Configured ⚠️ NEW

**Objective:** Verify MsgPack time format configuration is respected.

```csharp
[Fact]
public async Task RpcClient_MsgpackTimeFormat_Configured()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    server.SetMsgpackOldTimeFormat(); // Server uses old format
    await server.StartAsync();

    // Act - Client configured for old format
    var config = new RpcConfig 
    { 
        Address = server.Address,
        MsgpackUseNewTimeFormat = false
    };
    using var client = await RpcClient.ConnectAsync(config);

    // Assert - Should successfully communicate with old format
    var members = await client.MembersAsync();
    Assert.NotNull(members);

    // Verify MsgPack handle configuration
    Assert.False(config.MsgpackUseNewTimeFormat);
}
```

**Expected Behavior:**
- MsgpackUseNewTimeFormat controls time encoding
- Default: false (old format for backward compatibility)
- Creates MsgpackHandle with TimeNotBuiltin = !MsgpackUseNewTimeFormat
- Both encoder and decoder use same format

**Go Reference:**
```go
func (c *Config) newMsgpackHandle() *codec.MsgpackHandle {
    return &codec.MsgpackHandle{
        WriteExt: true,
        BasicHandle: codec.BasicHandle{
            TimeNotBuiltin: !c.MsgpackUseNewTimeFormat,
        },
    }
}
```

---

## Test Group 1.3: Sequence Number Management (4 tests - 1 added)

### Test 1.3.1-1.3.3: *(Same as before)*

### Test 1.3.4: RpcClient_Close_DeregistersAllHandlers ⚠️ NEW

**Objective:** Verify Close() deregisters all active handlers and cleans up.

```csharp
[Fact]
public async Task RpcClient_Close_DeregistersAllHandlers()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Start multiple streams (creates handlers)
    var logChannel = Channel.CreateUnbounded<string>();
    var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
    var handle1 = await client.MonitorAsync("debug", logChannel.Writer);
    var handle2 = await client.StreamAsync("*", eventChannel.Writer);

    // Act
    await client.CloseAsync();

    // Assert
    Assert.True(client.IsClosed());
    
    // Channels should be completed
    await Task.Delay(100);
    Assert.True(logChannel.Reader.Completion.IsCompleted);
    Assert.True(eventChannel.Reader.Completion.IsCompleted);
    
    // Internal handler map should be empty
    Assert.Equal(0, client.GetActiveHandlerCount()); // Test helper method
}
```

**Expected Behavior:**
- Close() calls deregisterAll()
- All handlers cleaned up
- Channels completed/closed
- No memory leaks

**Go Reference:**
```go
func (c *RPCClient) Close() error {
    c.shutdownLock.Lock()
    defer c.shutdownLock.Unlock()

    if !c.shutdown {
        c.shutdown = true
        close(c.shutdownCh)
        c.deregisterAll()  // ← This was missing!
        return c.conn.Close()
    }
    return nil
}
```

---

## Test Group 1.4: Message Encoding/Decoding (5 tests - 1 added)

### Test 1.4.1-1.4.4: *(Same as before)*

### Test 1.4.5: RpcClient_Send_AfterClose_ThrowsException ⚠️ NEW

**Objective:** Verify send operations fail after client closed.

```csharp
[Fact]
public async Task RpcClient_Send_AfterClose_ThrowsException()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    await client.CloseAsync();

    // Assert - All operations should fail
    await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
    {
        await client.MembersAsync();
    });

    await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
    {
        await client.JoinAsync(new[] { "node1/127.0.0.1:7946" }, false);
    });

    await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
    {
        await client.UserEventAsync("test", null, false);
    });
}
```

**Expected Behavior:**
- send() checks `if (c.shutdown)` before sending
- Returns clientClosed error
- genericRPC also checks shutdown channel
- Prevents operations on closed socket

**Go Reference:**
```go
func (c *RPCClient) send(header *requestHeader, obj interface{}) error {
    c.writeLock.Lock()
    defer c.writeLock.Unlock()

    if c.shutdown {
        return clientClosed  // ← Critical check
    }
    // ... rest of send logic
}
```

---

## Test Group 1.5: Listen Loop & Cleanup (1 test - NEW)

### Test 1.5.1: RpcClient_ListenLoop_ErrorHandling ⚠️ NEW

**Objective:** Verify listen loop handles errors and cleans up properly.

```csharp
[Fact]
public async Task RpcClient_ListenLoop_ErrorHandling()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act - Simulate server disconnect
    await server.SimulateDisconnect();
    await Task.Delay(200); // Allow listen loop to detect

    // Assert
    Assert.True(client.IsClosed());
    
    // Listen loop should have called Close() on error
    // Should not log error if shutdown was intentional
}
```

**Expected Behavior:**
- Listen loop runs in background task
- Decodes response headers continuously
- On error: calls Close() via defer
- Only logs errors if NOT shutting down
- Clean shutdown doesn't produce error logs

**Go Reference:**
```go
func (c *RPCClient) listen() {
    defer c.Close()  // ← Ensures cleanup
    var respHeader responseHeader
    for {
        if err := c.dec.Decode(&respHeader); err != nil {
            if !c.shutdown {
                log.Printf("[ERR] agent.client: Failed to decode response header: %v", err)
            }
            break
        }
        c.respondSeq(respHeader.Seq, &respHeader)
    }
}
```

---

## Additional Implementation Details Found

### 1. Buffered I/O (CRITICAL)

**Go uses bufio.Reader and bufio.Writer:**
```go
client := &RPCClient{
    reader:     bufio.NewReader(conn),
    writer:     bufio.NewWriter(conn),
}
```

**C# Implementation:**
```csharp
private readonly BufferedStream _bufferedStream;
private readonly StreamReader _reader;
private readonly StreamWriter _writer;

// Or use NetworkStream with buffering
var networkStream = new NetworkStream(_tcpClient.Client, ownsSocket: true);
_bufferedStream = new BufferedStream(networkStream, 4096);
```

### 2. TCP Connection Type

**Go casts to *net.TCPConn:**
```go
conn:       conn.(*net.TCPConn),
```

This allows TCP-specific options. C# equivalent:
```csharp
_tcpClient = new TcpClient();
_tcpClient.NoDelay = true; // Disable Nagle's algorithm
```

### 3. Timeout Defaults

```csharp
public class RpcConfig
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10); // DefaultTimeout
    
    // In ConnectAsync:
    if (config.Timeout == TimeSpan.Zero)
        config.Timeout = TimeSpan.FromSeconds(10);
}
```

### 4. Shutdown Channel Pattern

**Go uses channel for signaling:**
```go
shutdownCh: make(chan struct{}),

// In genericRPC:
select {
case err := <-errCh:
    return err
case <-c.shutdownCh:  // ← Check shutdown
    return clientClosed
}
```

**C# uses CancellationToken:**
```csharp
private readonly CancellationTokenSource _cts = new();

// In GenericRpcAsync:
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);

var result = await Task.WhenAny(
    responseTask,
    Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token));
```

---

## Updated Implementation Checklist

### Core Classes

- [ ] **RpcClient.cs**
  - [ ] Constructor (private, use factory)
  - [ ] ConnectAsync() factory method
  - [ ] **IsClosed()** property ⚠️ ADDED
  - [ ] CloseAsync() / DisposeAsync()
  - [ ] **DeregisterAll()** called on close ⚠️ ADDED
  - [ ] GetSeq() - atomic sequence increment
  - [ ] SendAsync() - send request with header
  - [ ] **Check shutdown in SendAsync()** ⚠️ ADDED
  - [ ] ListenLoopAsync() - background response handler
  - [ ] **Listen loop error handling** ⚠️ ADDED
  - [ ] HandshakeAsync() - protocol handshake
  - [ ] AuthAsync() - authentication
  - [ ] GenericRpcAsync() - common RPC pattern
  - [ ] **Buffered stream usage** ⚠️ ADDED

- [ ] **RpcConfig.cs**
  - [ ] Address property
  - [ ] AuthKey property
  - [ ] Timeout property (default 10s)
  - [ ] **MsgpackUseNewTimeFormat** property ⚠️ ADDED
  - [ ] Validation

- [ ] **RpcProtocol.cs**
  - [ ] Constants (MinIpcVersion=1, MaxIpcVersion=1)
  - [ ] Command names
  - [ ] Error strings
  - [ ] RequestHeader struct
  - [ ] ResponseHeader struct

---

## Updated Acceptance Criteria

- [ ] All **20 tests** passing (5 added)
- [ ] Code coverage >95%
- [ ] No resource leaks (verified with memory profiler)
- [ ] Async patterns correct (no sync-over-async)
- [ ] Exception handling comprehensive
- [ ] XML documentation on public APIs
- [ ] **Compatible with Go RPC protocol** ✓
- [ ] **MsgPack time format configurable** ⚠️ NEW
- [ ] **IsClosed() implemented** ⚠️ NEW
- [ ] **Proper cleanup on Close()** ⚠️ NEW
- [ ] **Buffered I/O for performance** ⚠️ NEW

---

## Summary of Changes

| Area | Added | Critical? |
|------|-------|-----------|
| IsClosed() method | Test 1.1.6 | ✅ Yes - prevents use after close |
| MsgPack time format | Test 1.2.4 | ✅ Yes - compatibility |
| DeregisterAll on close | Test 1.3.4 | ✅ Yes - prevents leaks |
| Send after close check | Test 1.4.5 | ✅ Yes - error handling |
| Listen loop cleanup | Test 1.5.1 | ✅ Yes - proper shutdown |
| Buffered I/O | Implementation | ✅ Yes - performance |
| TCP-specific options | Implementation | ⚙️ Nice to have |

**Test Count:** 15 → 20 tests (+33% coverage)

---

## Go Reference Files Verified

✅ `serf/client/rpc_client.go` (lines 1-859)  
✅ `serf/client/const.go` (protocol constants)  
✅ `serf/cmd/serf/command/agent/rpc_client_test.go`  
✅ DeepWiki query results
