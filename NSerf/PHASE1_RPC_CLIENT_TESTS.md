# Phase 1: RPC Client Foundation - Detailed Test Specification

**Timeline:** Week 1  
**Test Count:** 15 tests  
**Focus:** Connection, handshake, authentication, message encoding

---

## Test Group 1.1: Connection and Handshake (5 tests)

### Test 1.1.1: RpcClient_Connect_SuccessfulHandshake

**Objective:** Verify RPC client can establish connection and complete handshake.

```csharp
[Fact]
public async Task RpcClient_Connect_SuccessfulHandshake()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    var config = new RpcConfig 
    { 
        Address = server.Address,
        Timeout = TimeSpan.FromSeconds(10)
    };

    // Act
    using var client = await RpcClient.ConnectAsync(config);

    // Assert
    Assert.True(client.IsConnected);
    Assert.Equal(1, server.HandshakeCount);
    Assert.Equal(RpcProtocol.MaxIpcVersion, server.LastHandshakeVersion);
}
```

**Expected Behavior:**
- TCP connection established to server
- Handshake request sent with version=1
- Handshake response received
- Client marked as connected

---

### Test 1.1.2: RpcClient_Connect_InvalidAddress_ThrowsException

**Objective:** Verify appropriate exception on invalid address.

```csharp
[Fact]
public async Task RpcClient_Connect_InvalidAddress_ThrowsException()
{
    // Arrange
    var config = new RpcConfig { Address = "invalid:address" };

    // Act & Assert
    await Assert.ThrowsAsync<RpcException>(async () =>
    {
        await RpcClient.ConnectAsync(config);
    });
}
```

**Expected Behavior:**
- Connection attempt fails
- RpcException thrown with meaningful message
- No resources leaked

---

### Test 1.1.3: RpcClient_Connect_Timeout_ThrowsException

**Objective:** Verify timeout handling during connection.

```csharp
[Fact]
public async Task RpcClient_Connect_Timeout_ThrowsException()
{
    // Arrange
    using var server = new NonResponsiveMockServer("127.0.0.1:0");
    await server.StartAsync();
    var config = new RpcConfig 
    { 
        Address = server.Address,
        Timeout = TimeSpan.FromMilliseconds(100)
    };

    // Act & Assert
    var exception = await Assert.ThrowsAsync<RpcTimeoutException>(async () =>
    {
        await RpcClient.ConnectAsync(config);
    });

    Assert.Contains("timeout", exception.Message, StringComparison.OrdinalIgnoreCase);
}
```

**Expected Behavior:**
- Connection times out after specified duration
- RpcTimeoutException thrown
- Connection properly closed

---

### Test 1.1.4: RpcClient_Handshake_UnsupportedVersion_ThrowsException

**Objective:** Verify rejection of unsupported protocol version.

```csharp
[Fact]
public async Task RpcClient_Handshake_UnsupportedVersion_ThrowsException()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    server.SetHandshakeResponse(unsupportedVersion: true);
    await server.StartAsync();
    var config = new RpcConfig { Address = server.Address };

    // Act & Assert
    var exception = await Assert.ThrowsAsync<RpcException>(async () =>
    {
        await RpcClient.ConnectAsync(config);
    });

    Assert.Contains("Unsupported IPC version", exception.Message);
}
```

**Expected Behavior:**
- Handshake response indicates unsupported version
- Client throws RpcException
- Connection closed

---

### Test 1.1.5: RpcClient_Close_MultipleCalls_NoException

**Objective:** Verify idempotent close operation.

```csharp
[Fact]
public async Task RpcClient_Close_MultipleCalls_NoException()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    await client.CloseAsync();
    await client.CloseAsync(); // Second close
    await client.CloseAsync(); // Third close

    // Assert
    Assert.False(client.IsConnected);
    // No exception thrown
}
```

**Expected Behavior:**
- First close shuts down connection
- Subsequent closes are no-ops
- No exceptions thrown

---

## Test Group 1.2: Authentication (3 tests)

### Test 1.2.1: RpcClient_Auth_WithValidKey_Succeeds

**Objective:** Verify successful authentication with valid key.

```csharp
[Fact]
public async Task RpcClient_Auth_WithValidKey_Succeeds()
{
    // Arrange
    const string authKey = "test-auth-key";
    using var server = new MockRpcServer("127.0.0.1:0");
    server.RequireAuth(authKey);
    await server.StartAsync();
    
    var config = new RpcConfig 
    { 
        Address = server.Address,
        AuthKey = authKey
    };

    // Act
    using var client = await RpcClient.ConnectAsync(config);
    var members = await client.MembersAsync(); // Test subsequent command

    // Assert
    Assert.True(server.LastAuthSuccess);
    Assert.NotNull(members); // Command succeeded after auth
}
```

**Expected Behavior:**
- Auth command sent after handshake
- Server validates key
- Client marked as authenticated
- Subsequent commands allowed

---

### Test 1.2.2: RpcClient_Auth_WithInvalidKey_ThrowsException

**Objective:** Verify authentication failure with wrong key.

```csharp
[Fact]
public async Task RpcClient_Auth_WithInvalidKey_ThrowsException()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    server.RequireAuth("correct-key");
    await server.StartAsync();
    
    var config = new RpcConfig 
    { 
        Address = server.Address,
        AuthKey = "wrong-key"
    };

    // Act & Assert
    var exception = await Assert.ThrowsAsync<RpcAuthException>(async () =>
    {
        await RpcClient.ConnectAsync(config);
    });

    Assert.Contains("Invalid authentication token", exception.Message);
}
```

**Expected Behavior:**
- Auth command sent with wrong key
- Server returns error
- RpcAuthException thrown
- Connection closed

---

### Test 1.2.3: RpcClient_Command_RequiresAuth_WithoutAuth_ThrowsException

**Objective:** Verify command rejection when auth required but not provided.

```csharp
[Fact]
public async Task RpcClient_Command_RequiresAuth_WithoutAuth_ThrowsException()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    server.RequireAuth("some-key"); // Server requires auth
    await server.StartAsync();
    
    var config = new RpcConfig 
    { 
        Address = server.Address
        // No AuthKey provided
    };

    // Act
    using var client = await RpcClient.ConnectAsync(config);
    
    // Assert
    var exception = await Assert.ThrowsAsync<RpcAuthException>(async () =>
    {
        await client.MembersAsync();
    });

    Assert.Contains("Authentication required", exception.Message);
}
```

**Expected Behavior:**
- Client connects without auth
- Command sent
- Server returns "auth required" error
- RpcAuthException thrown

---

## Test Group 1.3: Sequence Number Management (3 tests)

### Test 1.3.1: RpcClient_SequenceNumbers_MonotonicallyIncreasing

**Objective:** Verify sequence numbers increment monotonically.

```csharp
[Fact]
public async Task RpcClient_SequenceNumbers_MonotonicallyIncreasing()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    await client.MembersAsync();  // seq 1
    await client.MembersAsync();  // seq 2
    await client.MembersAsync();  // seq 3

    // Assert
    var sequences = server.ReceivedSequences;
    Assert.Equal(3, sequences.Count);
    Assert.Equal(1ul, sequences[0]); // First non-handshake/non-auth command
    Assert.Equal(2ul, sequences[1]);
    Assert.Equal(3ul, sequences[2]);
}
```

**Expected Behavior:**
- Each request gets unique, incrementing sequence
- No gaps in sequence numbers
- Thread-safe increment

---

### Test 1.3.2: RpcClient_SequenceNumbers_UniquePerRequest

**Objective:** Verify no sequence collisions in concurrent requests.

```csharp
[Fact]
public async Task RpcClient_SequenceNumbers_UniquePerRequest()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act - Send 10 concurrent requests
    var tasks = Enumerable.Range(0, 10).Select(_ => client.MembersAsync());
    await Task.WhenAll(tasks);

    // Assert
    var sequences = server.ReceivedSequences;
    var distinctSequences = sequences.Distinct().Count();
    Assert.Equal(sequences.Count, distinctSequences); // All unique
}
```

**Expected Behavior:**
- Concurrent requests get different sequence numbers
- No collisions
- Atomic increment mechanism

---

### Test 1.3.3: RpcClient_SequenceNumbers_MatchResponse

**Objective:** Verify response sequence matches request sequence.

```csharp
[Fact]
public async Task RpcClient_SequenceNumbers_MatchResponse()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    server.EnableSequenceTracking();
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    await client.MembersAsync();

    // Assert
    var requestSeq = server.LastRequestSequence;
    var responseSeq = server.LastResponseSequence;
    Assert.Equal(requestSeq, responseSeq);
}
```

**Expected Behavior:**
- Response header contains same seq as request
- Client matches response to request via sequence
- Proper request/response correlation

---

## Test Group 1.4: Message Encoding/Decoding (4 tests)

### Test 1.4.1: RpcClient_Send_EncodesHeaderAndBody

**Objective:** Verify MsgPack encoding of request.

```csharp
[Fact]
public async Task RpcClient_Send_EncodesHeaderAndBody()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    server.CaptureRawBytes();
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    await client.MembersAsync();

    // Assert
    var rawBytes = server.LastRawRequest;
    Assert.NotNull(rawBytes);
    
    // Decode and verify
    var decoded = MsgPackSerializer.Deserialize<RequestHeader>(rawBytes);
    Assert.Equal("members", decoded.Command);
    Assert.True(decoded.Seq > 0);
}
```

**Expected Behavior:**
- Request header encoded as MsgPack
- Optional body encoded as MsgPack
- Wire format compatible with Go implementation

---

### Test 1.4.2: RpcClient_Receive_DecodesResponse

**Objective:** Verify MsgPack decoding of response.

```csharp
[Fact]
public async Task RpcClient_Receive_DecodesResponse()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    server.SetMembersResponse(new[] 
    {
        new Member { Name = "node1", Status = "alive" },
        new Member { Name = "node2", Status = "alive" }
    });
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    var members = await client.MembersAsync();

    // Assert
    Assert.Equal(2, members.Length);
    Assert.Equal("node1", members[0].Name);
    Assert.Equal("node2", members[1].Name);
}
```

**Expected Behavior:**
- Response header decoded
- Response body decoded
- Objects populated correctly

---

### Test 1.4.3: RpcClient_Send_SetsWriteDeadline

**Objective:** Verify write timeout handling.

```csharp
[Fact]
public async Task RpcClient_Send_SetsWriteDeadline()
{
    // Arrange
    using var server = new SlowMockServer("127.0.0.1:0");
    server.SetWriteDelay(TimeSpan.FromSeconds(5)); // Simulate slow write
    await server.StartAsync();
    
    var config = new RpcConfig 
    { 
        Address = server.Address,
        Timeout = TimeSpan.FromMilliseconds(100) // Short timeout
    };
    using var client = await RpcClient.ConnectAsync(config);

    // Act & Assert
    await Assert.ThrowsAsync<RpcTimeoutException>(async () =>
    {
        await client.MembersAsync();
    });
}
```

**Expected Behavior:**
- Write deadline set before send
- Timeout triggers if write takes too long
- RpcTimeoutException thrown

---

### Test 1.4.4: RpcClient_Receive_HandlesErrors

**Objective:** Verify error handling from response.

```csharp
[Fact]
public async Task RpcClient_Receive_HandlesErrors()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    server.SetResponseError("Custom error message");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act & Assert
    var exception = await Assert.ThrowsAsync<RpcException>(async () =>
    {
        await client.MembersAsync();
    });

    Assert.Contains("Custom error message", exception.Message);
}
```

**Expected Behavior:**
- Response header contains error string
- Client converts to exception
- Error message preserved

---

## Implementation Checklist

### Core Classes

- [ ] **RpcClient.cs**
  - [ ] Constructor (private, use factory)
  - [ ] ConnectAsync() factory method
  - [ ] CloseAsync() / Dispose()
  - [ ] GetSeq() - atomic sequence increment
  - [ ] SendAsync() - send request with header
  - [ ] ListenLoop() - background response handler
  - [ ] HandshakeAsync() - protocol handshake
  - [ ] AuthAsync() - authentication
  - [ ] GenericRpcAsync() - common RPC pattern

- [ ] **RpcConfig.cs**
  - [ ] Address property
  - [ ] AuthKey property
  - [ ] Timeout property
  - [ ] MsgpackUseNewTimeFormat property
  - [ ] Validation

- [ ] **RpcProtocol.cs**
  - [ ] Constants (MinIpcVersion, MaxIpcVersion)
  - [ ] Command names
  - [ ] Error strings
  - [ ] RequestHeader struct
  - [ ] ResponseHeader struct

- [ ] **SeqHandler.cs**
  - [ ] ISeqHandler interface
  - [ ] Handle(ResponseHeader) method
  - [ ] Cleanup() method

- [ ] **SeqCallback.cs**
  - [ ] Implements ISeqHandler
  - [ ] Simple callback wrapper

### Helper Classes

- [ ] **Exceptions/RpcException.cs**
- [ ] **Exceptions/RpcAuthException.cs**
- [ ] **Exceptions/RpcTimeoutException.cs**

### Test Infrastructure

- [ ] **MockRpcServer.cs**
  - [ ] TCP listener
  - [ ] MsgPack codec
  - [ ] Request tracking
  - [ ] Configurable responses
  - [ ] Auth simulation

- [ ] **TestRpcFactory.cs**
  - [ ] Helper factory methods
  - [ ] Common test scenarios

---

## Implementation Notes

### MsgPack Configuration
```csharp
var options = MessagePackSerializerOptions.Standard
    .WithResolver(ContractlessStandardResolver.Instance);
```

### TcpClient Setup
```csharp
_tcpClient = new TcpClient();
_tcpClient.NoDelay = true; // Disable Nagle's algorithm
await _tcpClient.ConnectAsync(host, port, cancellationToken);
```

### Listen Loop Pattern
```csharp
private async Task ListenLoopAsync(CancellationToken cancellationToken)
{
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var header = await MessagePackSerializer.DeserializeAsync<ResponseHeader>(
                _stream, _msgpackOptions, cancellationToken);
            RespondToSequence(header.Seq, header);
        }
    }
    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
    {
        _logger?.LogError(ex, "Listen loop error");
    }
    finally
    {
        await CloseAsync();
    }
}
```

### Thread-Safe Sequence
```csharp
private ulong _seq = 0;

private ulong GetNextSeq()
{
    return (ulong)Interlocked.Increment(ref Unsafe.As<ulong, long>(ref _seq));
}
```

---

## Acceptance Criteria

- [ ] All 15 tests passing
- [ ] Code coverage >95%
- [ ] No resource leaks (verified with memory profiler)
- [ ] Async patterns correct (no sync-over-async)
- [ ] Exception handling comprehensive
- [ ] XML documentation on public APIs
- [ ] Compatible with Go RPC protocol

---

## Next Phase Preview

**Phase 2** will add all RPC command methods (Members, Join, Leave, Events, Keys, Queries) building on this foundation. The core infrastructure from Phase 1 makes Phase 2 straightforward.
