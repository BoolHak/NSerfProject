# Phase 8: CLI Commands & Integration - Detailed Test Specification

**Timeline:** Weeks 9-10 | **Tests:** 40 | **Focus:** Complete agent with CLI

---

## Files to Create
```
NSerf/NSerf/Agent/
├── Command.cs (~400 lines)
├── LogWriter.cs (~100 lines)
├── GatedWriter.cs (~50 lines)
├── LogLevels.cs (~50 lines)
├── AgentMdns.cs (~150 lines)
└── Util.cs (~50 lines)

NSerfTests/Agent/
├── CommandTests.cs (8)
├── LogManagementTests.cs (5)
├── SignalHandlingTests.cs (4)
├── ConfigReloadTests.cs (6)
├── MdnsTests.cs (5)
└── IntegrationTests.cs (12)
```

---

## Test Groups

### 8.1 Agent Command (8 tests)
1. Agent starts with config
2. Agent binds to specified address
3. Agent loads event scripts
4. Agent creates RPC server
5. Agent joins start_join nodes
6. Agent handles signals
7. Agent reload config
8. Agent graceful shutdown

### 8.2 Log Management (5 tests)
1. Log writer buffers logs
2. Log level filtering
3. Gated writer releases on signal
4. Syslog output (Linux only)
5. Log rotation

### 8.3 Signal Handling (4 tests)
1. SIGTERM triggers graceful shutdown
2. SIGINT triggers graceful shutdown
3. SIGHUP triggers config reload
4. Double signal forces shutdown

### 8.4 Config Reload (6 tests)
1. Reload updates log level
2. Reload updates event scripts
3. Reload preserves connection
4. Reload validates config
5. Invalid config rejected
6. Reload without restart

### 8.5 mDNS Discovery (5 tests)
1. mDNS advertises agent
2. mDNS discovers peers
3. Auto-join via mDNS
4. mDNS on specific interface
5. mDNS IPv4/IPv6

### 8.6 Integration Tests (12 tests)
1. Full agent lifecycle with RPC
2. Multi-node cluster formation
3. Event propagation end-to-end
4. Script execution integration
5. Graceful shutdown and restart
6. Tag persistence across restarts
7. Key rotation without downtime
8. Query broadcast and collection
9. Monitor/stream concurrent access
10. Configuration reload live
11. Failure recovery scenarios
12. Protocol compatibility with Go

---

## Integration Test Example

```csharp
[Fact]
public async Task Integration_FullAgentLifecycle()
{
    // Start agent 1
    var config1 = TestFactory.CreateConfig("node1");
    var agent1 = await Agent.CreateAsync(config1, new SerfConfig());
    await agent1.StartAsync();
    
    // Start IPC server
    var ipc1 = new AgentIpc(agent1, config1.RpcAddr);
    await ipc1.StartAsync();
    
    // Connect RPC client
    var client = await RpcClient.ConnectAsync(new RpcConfig 
    { 
        Address = config1.RpcAddr 
    });
    
    // Start agent 2
    var agent2 = await TestFactory.CreateAndStartAsync("node2");
    
    // Join via RPC
    var count = await client.JoinAsync(
        new[] { $"node2/{agent2.Config.BindAddr}" }, 
        false);
    Assert.Equal(1, count);
    
    // Verify members
    var members = await client.MembersAsync();
    Assert.Equal(2, members.Length);
    
    // Send user event
    await client.UserEventAsync("test", Encoding.UTF8.GetBytes("data"), false);
    await Task.Delay(200);
    
    // Graceful shutdown
    await client.LeaveAsync();
    await agent1.ShutdownAsync();
    await agent2.ShutdownAsync();
    await ipc1.ShutdownAsync();
}
```

---

## Cross-Platform Considerations

**Windows:**
- PowerShell scripts (.ps1)
- Windows Services support
- Event Log integration

**Linux:**
- Bash scripts (.sh)
- systemd integration
- Syslog output

**Both:**
- Graceful signal handling
- File path normalization
- Network interface detection

---

## Performance Targets

- Agent startup: <1 second
- RPC latency: <10ms
- Event propagation: <100ms in 3-node cluster
- Memory: <50MB base + 1MB per 1000 members
- 100 concurrent RPC clients

---

## Protocol Compatibility

Must interoperate with Go Serf:
- [ ] C# agent joins Go cluster
- [ ] Go agent joins C# cluster
- [ ] Events propagate between C# and Go
- [ ] Go RPC client controls C# agent
- [ ] C# RPC client controls Go agent
- [ ] Mixed cluster stable for hours

---

## Acceptance Criteria

- [ ] All 250+ tests passing
- [ ] Full agent functionality
- [ ] CLI commands work
- [ ] Script execution cross-platform
- [ ] Protocol compatible with Go
- [ ] Performance within targets
- [ ] Documentation complete
- [ ] Ready for production use

---

## Go Reference
- `serf/cmd/serf/command/agent/command.go`
- `serf/cmd/serf/command/agent/command_test.go`
- `serf/cmd/serf/command/agent/mdns.go`
