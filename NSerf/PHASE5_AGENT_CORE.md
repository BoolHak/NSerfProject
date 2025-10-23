# Phase 5: Agent Core - Detailed Test Specification

**Timeline:** Week 5 | **Tests:** 31 | **Focus:** Agent lifecycle, event handlers, Serf wrapper

---

## Files to Create
```
NSerf/NSerf/Agent/
├── Agent.cs (~500 lines)
├── IEventHandler.cs
├── AgentState.cs
└── Exceptions/

NSerfTests/Agent/
├── AgentLifecycleTests.cs (8)
├── AgentEventHandlerTests.cs (6)
├── AgentOperationsTests.cs (8)
├── AgentTagsTests.cs (5)
└── AgentKeyringTests.cs (4)
```

---

## Test Groups

### 5.1 Lifecycle (8 tests)
1. Create initializes agent
2. Start creates Serf instance
3. Start twice throws exception
4. Leave initiates graceful shutdown
5. Shutdown stops all processes
6. Shutdown idempotent
7. Shutdown before leave works
8. ShutdownChannel signals completion

### 5.2 Event Handlers (6 tests)
1. Register adds handler
2. Multiple handlers supported
3. Duplicate handler ignored
4. Deregister removes handler
5. Event loop dispatches to all
6. Handler exception doesn't stop loop

### 5.3 Serf Operations (8 tests)
1. Join delegates to Serf
2. Join with replay=true replays events
3. Join returns count
4. UserEvent broadcasts
5. Query initiates query
6. ForceLeave removes failed node
7. ForceLeavePrune prunes completely
8. UpdateTags modifies tags

### 5.4 Tags Management (5 tests)
1. Load tags file on create
2. Save tags file on update
3. UpdateTags adds new tags
4. UpdateTags deletes tags
5. Role tag special handling

### 5.5 Keyring Management (4 tests)
1. Load keyring file on create
2. InstallKey updates keyring file
3. RemoveKey updates keyring file
4. ListKeys returns all keys

---

## Key Implementation

```csharp
public class Agent : IDisposable
{
    private readonly AgentConfig _agentConfig;
    private readonly Config _serfConfig;
    private readonly Channel<Event> _eventChannel;
    private readonly HashSet<IEventHandler> _eventHandlers;
    private Serf? _serf;
    private bool _shutdown;
    
    public static async Task<Agent> CreateAsync(AgentConfig agentConfig, Config serfConfig)
    {
        // Load tags/keyring from files
        // Create event channel
        // Return agent (not started)
    }
    
    public async Task StartAsync()
    {
        _serf = await Serf.CreateAsync(_serfConfig);
        _ = Task.Run(EventLoop);
    }
    
    public async Task LeaveAsync() => await _serf.LeaveAsync();
    
    public async Task ShutdownAsync()
    {
        await _serf?.ShutdownAsync();
        _eventChannel.Writer.Complete();
    }
    
    private async Task EventLoop()
    {
        await foreach (var evt in _eventChannel.Reader.ReadAllAsync())
        {
            foreach (var handler in _eventHandlers.ToArray())
            {
                try { handler.HandleEvent(evt); }
                catch (Exception ex) { /* Log and continue */ }
            }
        }
    }
}
```

---

## Go Reference
- `serf/cmd/serf/command/agent/agent.go`
- `serf/cmd/serf/command/agent/agent_test.go`
