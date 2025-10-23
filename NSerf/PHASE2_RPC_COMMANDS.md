# Phase 2: RPC Client Commands - Detailed Test Specification

**Timeline:** Week 2  
**Test Count:** 29 tests  
**Focus:** Membership, Events, Keys, Queries, and other RPC commands

**Prerequisites:** Phase 1 complete (connection, handshake, auth working)

---

## Implementation Summary

### Files to Create/Update
```
NSerf/NSerf/Client/
├── RpcClient.cs (+500 lines - add all command methods)
├── Requests/
│   ├── JoinRequest.cs
│   ├── MembersFilteredRequest.cs
│   ├── EventRequest.cs
│   ├── ForceLeaveRequest.cs
│   ├── KeyRequest.cs
│   ├── QueryRequest.cs
│   ├── TagsRequest.cs
│   ├── RespondRequest.cs
│   ├── MonitorRequest.cs
│   ├── StreamRequest.cs
│   ├── StopRequest.cs
│   └── CoordinateRequest.cs
└── Responses/
    ├── JoinResponse.cs
    ├── MembersResponse.cs
    ├── KeyResponse.cs
    ├── CoordinateResponse.cs
    ├── Member.cs
    └── NodeResponse.cs

NSerfTests/Client/
├── RpcMembershipTests.cs (8 tests)
├── RpcEventTests.cs (4 tests)
├── RpcKeyTests.cs (8 tests)
├── RpcQueryTests.cs (5 tests)
└── RpcMiscTests.cs (4 tests)
```

---

## Test Group 2.1: Membership Commands (8 tests)

### Key Implementation Points
- Members() returns Member[] with all protocol/delegate versions
- MembersFiltered() uses regex for tags/name, exact match for status
- Join() with replay flag controls event replay
- ForceLeave() with Prune flag for complete removal

### Critical Test Scenarios from Go
1. Members after join shows 2 nodes
2. MembersFiltered by multiple tags requires ALL match
3. MembersFiltered by status ("alive", "failed", "left", "leaving")
4. Join returns count of successful joins
5. ForceLeave transitions failed→left
6. ForceLeavePrune removes from member list entirely

---

## Test Group 2.2: Event Commands (4 tests)

### Key Implementation Points
- UserEvent with name + payload (max 512 bytes combined)
- Coalesce flag merges events with same name
- Events delivered to all nodes via gossip

### Critical Test Scenarios from Go
1. UserEvent broadcasts to all nodes
2. Event handler receives name + payload
3. Coalesce reduces duplicate events
4. Empty payload is valid

---

## Test Group 2.3: Key Management (8 tests)

### Key Implementation Points
- Keys must be 32 bytes, base64 encoded
- InstallKey adds to keyring (idempotent)
- UseKey changes primary encryption key
- RemoveKey only works on non-primary keys
- ListKeys returns map[key]→nodeCount

### Critical Test Scenarios from Go
1. InstallKey with valid key succeeds
2. InstallKey idempotent (no error on duplicate)
3. UseKey changes primary
4. RemoveKey on primary fails
5. Invalid key format rejected
6. All operations fail if encryption disabled

---

## Test Group 2.4: Query Commands (5 tests)

### Key Implementation Points
- Query initiates cluster-wide request
- RequestAck flag controls acknowledgments
- Responses stream back via channels
- Timeout controls max query duration

### Critical Test Scenarios from Go
1. Query sends to all nodes
2. Acks received when RequestAck=true
3. Responses contain From + Payload
4. Respond() sends query response
5. Query times out after specified duration

---

## Test Group 2.5: Other Commands (4 tests)

### Commands
1. UpdateTags() - modify node tags
2. Stats() - retrieve agent statistics
3. GetCoordinate() - get network coordinate
4. Leave() - graceful shutdown

---

## Detailed Test Examples

### Test 2.1.1: Members Returns All Members

```csharp
[Fact]
public async Task RpcClient_Members_ReturnsAllMembers()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    var expectedMembers = new[]
    {
        new Member 
        { 
            Name = "node1",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 7946,
            Status = "alive",
            Tags = new Dictionary<string, string> { ["role"] = "web" },
            ProtocolMin = 2, ProtocolMax = 5, ProtocolCur = 5,
            DelegateMin = 2, DelegateMax = 5, DelegateCur = 5
        }
    };
    server.SetMembersResponse(expectedMembers);
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    var members = await client.MembersAsync();

    // Assert
    Assert.Single(members);
    Assert.Equal("node1", members[0].Name);
    Assert.Equal("alive", members[0].Status);
    Assert.Equal("web", members[0].Tags["role"]);
}
```

### Test 2.3.1: InstallKey Adds Key

```csharp
[Fact]
public async Task RpcClient_InstallKey_AddsNewKey()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    server.EnableEncryption("existing-key");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    var newKey = "5K9OtfP7efFrNKe5WCQvXvnaXJ5cWP0SvXiwe0kkjM4=";
    var messages = await client.InstallKeyAsync(newKey);

    // Assert
    Assert.Empty(messages); // No errors
    Assert.True(server.KeyringContains(newKey));
}
```

### Test 2.4.1: Query Sends Query

```csharp
[Fact]
public async Task RpcClient_Query_SendsQuery()
{
    // Arrange
    using var server = new MockRpcServer("127.0.0.1:0");
    await server.StartAsync();
    using var client = await RpcClient.ConnectAsync(new RpcConfig { Address = server.Address });

    // Act
    var ackCh = Channel.CreateUnbounded<string>();
    var respCh = Channel.CreateUnbounded<NodeResponse>();
    var queryParams = new QueryParam
    {
        Name = "check-status",
        Payload = Encoding.UTF8.GetBytes("health"),
        RequestAck = true,
        Timeout = TimeSpan.FromSeconds(5),
        AckCh = ackCh.Writer,
        RespCh = respCh.Writer
    };

    await client.QueryAsync(queryParams);

    // Assert
    var request = server.LastQueryRequest;
    Assert.Equal("check-status", request.Name);
    Assert.True(request.RequestAck);
}
```

---

## Implementation Checklist

### RpcClient.cs Methods to Add

- [ ] **MembersAsync()** → Member[]
- [ ] **MembersFilteredAsync(tags, status, name)** → Member[]
- [ ] **JoinAsync(addresses, replay)** → int
- [ ] **LeaveAsync()** → void
- [ ] **ForceLeaveAsync(node)** → void
- [ ] **ForceLeavePruneAsync(node)** → void
- [ ] **UserEventAsync(name, payload, coalesce)** → void
- [ ] **UpdateTagsAsync(tags, deleteTags)** → void
- [ ] **InstallKeyAsync(key)** → Dictionary<string, string>
- [ ] **UseKeyAsync(key)** → Dictionary<string, string>
- [ ] **RemoveKeyAsync(key)** → Dictionary<string, string>
- [ ] **ListKeysAsync()** → (Dictionary<string, int>, int, Dictionary<string, string>)
- [ ] **QueryAsync(params)** → void
- [ ] **RespondAsync(id, payload)** → void
- [ ] **StatsAsync()** → Dictionary<string, Dictionary<string, string>>
- [ ] **GetCoordinateAsync(node)** → Coordinate?

### Request/Response DTOs

- [ ] Create all 12 request classes
- [ ] Create all 6 response classes
- [ ] Member class with all fields
- [ ] QueryParam class
- [ ] NodeResponse class

### Common Pattern

All commands follow this pattern:
```csharp
public async Task<TResponse> CommandAsync(TRequest request)
{
    var header = new RequestHeader
    {
        Command = "command-name",
        Seq = GetNextSeq()
    };
    
    return await GenericRpcAsync<TRequest, TResponse>(header, request);
}
```

---

## Acceptance Criteria

- [ ] All 29 tests passing
- [ ] All RPC commands implemented
- [ ] Request/Response DTOs match Go protocol
- [ ] MsgPack serialization works for all types
- [ ] Error handling for all commands
- [ ] Code coverage >95%
- [ ] XML documentation complete

---

## Go Reference

**Test File:** `serf/cmd/serf/command/agent/rpc_client_test.go`  
**Key Tests:**
- TestRPCClientMembers
- TestRPCClientMembersFiltered
- TestRPCClientJoin
- TestRPCClientUserEvent
- TestRPCClient_Keys
- TestRPCClientQuery

**Protocol:** `serf/client/const.go`  
**Client:** `serf/client/rpc_client.go`

---

## Next Phase Preview

**Phase 3** will implement streaming operations (Monitor, Stream, Stop) which use continuous data flow patterns with Channel<T> in C#.
