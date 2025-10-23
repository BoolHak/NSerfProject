# Phase 7: Event Handlers & Script Execution - Detailed Test Specification

**Timeline:** Week 8 | **Tests:** 25 | **Focus:** Script invocation for events

---

## Files to Create
```
NSerf/NSerf/Agent/
├── ScriptEventHandler.cs (~150 lines)
├── ScriptInvoker.cs (~200 lines)
└── EventScript.cs (update)

NSerfTests/Agent/
├── EventFilterTests.cs (5)
├── EventFilterMatchTests.cs (6)
├── ScriptExecutionTests.cs (8)
├── ScriptEnvironmentTests.cs (4)
└── QueryResponseTests.cs (2)
```

---

## Test Groups

### 7.1 Event Filter Parsing (5 tests)
1. Parse simple script (all events)
2. Parse with event filter
3. Parse user event filter
4. Parse query filter
5. Invalid filter throws

### 7.2 Event Filter Matching (6 tests)
1. Wildcard matches all
2. member-join matches only joins
3. member-leave matches only leaves
4. user event matches
5. user:name filters by name
6. query:name filters by name

### 7.3 Script Execution (8 tests)
1. Script executes on event
2. Multiple scripts execute
3. Script receives environment vars
4. Script receives stdin
5. Script stdout captured (for queries)
6. Script timeout handled
7. Script failure logged
8. Cross-platform execution

### 7.4 Environment Variables (4 tests)
1. SERF_EVENT set correctly
2. SERF_SELF_NAME/ADDR/ROLE set
3. SERF_TAG_* for all tags
4. SERF_USER_EVENT/LTIME for user events

### 7.5 Query Response (2 tests)
1. Script stdout becomes query response
2. Empty stdout handled

---

## Key Implementation

```csharp
public class ScriptEventHandler : IEventHandler
{
    private readonly List<EventScript> _scripts;
    private readonly Func<Member> _selfFunc;
    
    public void HandleEvent(Event evt)
    {
        var self = _selfFunc();
        
        foreach (var script in _scripts)
        {
            if (!script.Filter.Invoke(evt))
                continue;
                
            _ = Task.Run(() => InvokeScript(script, self, evt));
        }
    }
    
    private async Task InvokeScript(EventScript script, Member self, Event evt)
    {
        var envVars = BuildEnvironmentVariables(self, evt);
        var stdin = BuildStdin(evt);
        
        var result = await ScriptInvoker.ExecuteAsync(
            script.Script,
            envVars,
            stdin,
            timeout: TimeSpan.FromSeconds(30));
        
        if (evt is Query query)
        {
            await query.RespondAsync(Encoding.UTF8.GetBytes(result.Stdout));
        }
    }
}
```

---

## Platform Differences

**Windows:**
- Use `cmd.exe /c` or PowerShell for .ps1
- Process.Start with proper shell

**Linux:**
- Use `/bin/sh` or `/bin/bash`
- Check execute permission

---

## Go Reference
- `serf/cmd/serf/command/agent/event_handler.go`
- `serf/cmd/serf/command/agent/invoke.go`
- `serf/cmd/serf/command/agent/event_handler_test.go`
