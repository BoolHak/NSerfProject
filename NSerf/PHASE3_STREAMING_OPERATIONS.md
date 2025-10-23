# Phase 3: Streaming Operations - Detailed Test Specification

**Timeline:** Week 3  
**Test Count:** 17 tests  
**Focus:** Monitor (logs), Stream (events), Stop, and continuous data flow

**Prerequisites:** Phase 2 complete (all RPC commands working)

---

## Implementation Summary

### Files to Create
```
NSerf/NSerf/Client/
├── StreamHandle.cs (opaque handle type)
└── Handlers/
    ├── MonitorHandler.cs (~120 lines)
    ├── StreamHandler.cs (~120 lines)
    └── QueryHandler.cs (~100 lines)

NSerfTests/Client/
├── RpcMonitorTests.cs (6 tests)
├── RpcStreamTests.cs (8 tests)
└── RpcStopTests.cs (3 tests)
```

---

## Test Group 3.1: Monitor (Log Streaming) - 6 tests

### Key Concepts
- Monitor subscribes to agent logs
- Log level filter (DEBUG, INFO, WARN, ERROR)
- Logs streamed via Channel<string>
- Stop() terminates stream

### Test 3.1.1: Monitor Streams Logs

```csharp
[Fact]
public async Task RpcClient_Monitor_StreamsLogs()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    server.EnableLogging();
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    var logChannel = Channel.CreateUnbounded<string>();
    var handle = await client.MonitorAsync("debug", logChannel.Writer);

    // Trigger some logs
    server.LogMessage("[DEBUG] test message");
    await Task.Delay(100);

    // Assert
    Assert.True(logChannel.Reader.TryRead(out var log));
    Assert.Contains("test message", log);

    // Cleanup
    await client.StopAsync(handle);
}
```

**Expected Behavior:**
- Monitor request sent with log level
- Server streams logs via response
- Each log is a separate message
- Channel never blocks (unbounded or with backpressure)

---

### Test 3.1.2: Monitor Filters By LogLevel

```csharp
[Fact]
public async Task RpcClient_Monitor_FiltersLogLevel()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act - Subscribe at INFO level
    var logChannel = Channel.CreateUnbounded<string>();
    var handle = await client.MonitorAsync("INFO", logChannel.Writer);

    server.LogMessage("[DEBUG] should not see this");
    server.LogMessage("[INFO] should see this");
    server.LogMessage("[ERROR] should see this");
    await Task.Delay(100);

    // Assert
    var logs = new List<string>();
    while (logChannel.Reader.TryRead(out var log))
        logs.Add(log);

    Assert.Equal(2, logs.Count);
    Assert.DoesNotContain(logs, l => l.Contains("should not see"));
}
```

**Expected Behavior:**
- Log levels: DEBUG < INFO < WARN < ERROR
- Filter includes level and above
- Case-insensitive level names

---

### Test 3.1.3: Monitor Stop Terminates Stream

```csharp
[Fact]
public async Task RpcClient_Monitor_Stop_StopsStreaming()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });
    var logChannel = Channel.CreateUnbounded<string>();
    var handle = await client.MonitorAsync("debug", logChannel.Writer);

    // Act
    await client.StopAsync(handle);
    await Task.Delay(100);

    server.LogMessage("[DEBUG] after stop");
    await Task.Delay(100);

    // Assert
    // No new messages after stop
    var initialCount = 0;
    while (logChannel.Reader.TryRead(out _)) initialCount++;

    await Task.Delay(100);
    var afterCount = 0;
    while (logChannel.Reader.TryRead(out _)) afterCount++;

    Assert.Equal(initialCount, afterCount); // No new messages
}
```

---

## Test Group 3.2: Event Streaming - 8 tests

### Key Concepts
- Stream subscribes to Serf events
- Filter types: "*", "member-join", "member-leave", "user", "user:name", "query", "query:name"
- Events delivered as map[string]interface{}
- Multiple concurrent streams supported

### Test 3.2.1: Stream All Events

```csharp
[Fact]
public async Task RpcClient_Stream_AllEvents_ReceivesAll()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
    var handle = await client.StreamAsync("*", eventChannel.Writer);

    // Trigger events
    await client.UserEventAsync("deploy", Encoding.UTF8.GetBytes("v1.0"), false);
    await Task.Delay(100);

    // Assert
    Assert.True(eventChannel.Reader.TryRead(out var evt));
    Assert.Equal("user", evt["Event"]);
    Assert.Equal("deploy", evt["Name"]);

    await client.StopAsync(handle);
}
```

**Expected Behavior:**
- Filter "*" receives all event types
- Events have Event, LTime, and type-specific fields
- User events: Name, Payload, Coalesce
- Member events: Event, Members[]
- Query events: ID, Name, Payload

---

### Test 3.2.2: Stream User Events Only

```csharp
[Fact]
public async Task RpcClient_Stream_UserEvent_ReceivesOnlyUser()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
    var handle = await client.StreamAsync("user", eventChannel.Writer);

    // Trigger mixed events
    server.TriggerMemberJoin("node2");
    await client.UserEventAsync("test", null, false);
    await Task.Delay(100);

    // Assert
    var events = new List<Dictionary<string, object>>();
    while (eventChannel.Reader.TryRead(out var evt))
        events.Add(evt);

    Assert.All(events, e => Assert.Equal("user", e["Event"]));

    await client.StopAsync(handle);
}
```

---

### Test 3.2.3: Stream User Event By Name

```csharp
[Fact]
public async Task RpcClient_Stream_UserEventByName_FiltersCorrectly()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act - Filter for "user:deploy" events only
    var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
    var handle = await client.StreamAsync("user:deploy", eventChannel.Writer);

    await client.UserEventAsync("deploy", Encoding.UTF8.GetBytes("v1"), false);
    await client.UserEventAsync("restart", Encoding.UTF8.GetBytes("v2"), false);
    await Task.Delay(100);

    // Assert
    var events = new List<Dictionary<string, object>>();
    while (eventChannel.Reader.TryRead(out var evt))
        events.Add(evt);

    Assert.All(events, e => Assert.Equal("deploy", e["Name"]));

    await client.StopAsync(handle);
}
```

**Expected Behavior:**
- "user:name" filter matches specific user event name
- "query:name" filter matches specific query name
- Filters are exact match, not regex

---

### Test 3.2.4: Stream Member Events

```csharp
[Fact]
public async Task RpcClient_Stream_MemberJoin_ReceivesMemberInfo()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
    var handle = await client.StreamAsync("member-join", eventChannel.Writer);

    var newMember = new Member 
    { 
        Name = "node2", 
        Addr = IPAddress.Parse("127.0.0.2"), 
        Port = 7946,
        Status = "alive"
    };
    server.TriggerMemberJoin(newMember);
    await Task.Delay(100);

    // Assert
    Assert.True(eventChannel.Reader.TryRead(out var evt));
    Assert.Equal("member-join", evt["Event"]);
    var members = evt["Members"] as List<object>;
    Assert.NotNull(members);
    Assert.Single(members);

    await client.StopAsync(handle);
}
```

---

## Test Group 3.3: Stop Operations - 3 tests

### Test 3.3.1: Stop Valid Handle

```csharp
[Fact]
public async Task RpcClient_Stop_ValidHandle_Succeeds()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });
    var channel = Channel.CreateUnbounded<string>();
    var handle = await client.MonitorAsync("debug", channel.Writer);

    // Act
    await client.StopAsync(handle);

    // Assert
    var stopRequest = server.LastStopRequest;
    Assert.Equal((ulong)handle, stopRequest.Stop);
}
```

---

## Implementation Details

### StreamHandle Type

```csharp
public readonly struct StreamHandle
{
    private readonly ulong _value;

    internal StreamHandle(ulong value) => _value = value;

    public static implicit operator ulong(StreamHandle handle) => handle._value;
    internal static StreamHandle FromSeq(ulong seq) => new StreamHandle(seq);
}
```

### MonitorHandler Pattern

```csharp
private class MonitorHandler : ISeqHandler
{
    private readonly RpcClient _client;
    private readonly ChannelWriter<string> _logWriter;
    private readonly ulong _seq;
    private bool _init;

    public void Handle(ResponseHeader header)
    {
        if (!_init)
        {
            _init = true;
            if (!string.IsNullOrEmpty(header.Error))
                throw new RpcException(header.Error);
            return;
        }

        var logRecord = MessagePackSerializer.Deserialize<LogRecord>(_client._stream);
        
        if (!_logWriter.TryWrite(logRecord.Log))
        {
            // Log dropped - channel full
        }
    }

    public void Cleanup()
    {
        _logWriter.Complete();
    }
}
```

### Client Methods

```csharp
public async Task<StreamHandle> MonitorAsync(string logLevel, ChannelWriter<string> logWriter)
{
    var seq = GetNextSeq();
    var header = new RequestHeader { Command = "monitor", Seq = seq };
    var request = new MonitorRequest { LogLevel = logLevel };

    var initChannel = Channel.CreateBounded<Exception?>(1);
    var handler = new MonitorHandler(_client, logWriter, seq, initChannel.Writer);
    
    RegisterHandler(seq, handler);
    
    try
    {
        await SendAsync(header, request);
        
        var error = await initChannel.Reader.ReadAsync(_cts.Token);
        if (error != null) throw error;
        
        return StreamHandle.FromSeq(seq);
    }
    catch
    {
        DeregisterHandler(seq);
        throw;
    }
}

public async Task StopAsync(StreamHandle handle)
{
    // First deregister locally
    DeregisterHandler((ulong)handle);
    
    // Then tell server
    var header = new RequestHeader { Command = "stop", Seq = GetNextSeq() };
    var request = new StopRequest { Stop = (ulong)handle };
    await GenericRpcAsync(header, request, null);
}
```

---

## Acceptance Criteria

- [ ] All 17 tests passing
- [ ] Monitor streaming works
- [ ] Event streaming with all filter types
- [ ] Stop terminates streams cleanly
- [ ] No memory leaks (handlers cleaned up)
- [ ] Channel backpressure handled
- [ ] Multiple concurrent streams supported
- [ ] Code coverage >95%

---

## Go Reference

**Tests:** `serf/cmd/serf/command/agent/rpc_client_test.go`
- TestRPCClientMonitor
- TestRPCClientStream_User
- TestRPCClientStream_Member
- TestRPCClientStream_Query

**Handlers:** `serf/client/rpc_client.go`
- monitorHandler
- streamHandler
- queryHandler

---

## Next Phase Preview

**Phase 4** switches focus to the Agent configuration system - JSON parsing, validation, tags/keyring file persistence.
