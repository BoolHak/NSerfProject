# State Machine Pattern Discussion for Serf
**Recognizing the State Machine in Member Lifecycle**

---

## The Observation

Member status management is fundamentally a **state machine**:

```
States: Alive, Leaving, Left, Failed
Transitions: Governed by events (join intent, leave intent, memberlist notifications)
Guards: Lamport times, status checks, origin of message
```

**Current Problem:** Transition logic is scattered across multiple methods.

---

## Current State Transitions

### Identified States

```csharp
public enum MemberStatus
{
    None = 0,
    Alive = 1,      // Active member
    Leaving = 2,    // Graceful leave initiated
    Left = 3,       // Gracefully departed
    Failed = 4      // Detected as failed by memberlist
}
```

### Valid Transitions (from code analysis)

```
Alive ──────────────────────────────► Failed
  │                                      │
  │ (leave intent)                       │ (remove failed node)
  ▼                                      ▼
Leaving ─────────────────────────────► Left
  │                                      ▲
  │ (newer join intent)                  │
  └──────────────────────────────────────┘
         (refutation)
         
CRITICAL RULE: Left/Failed → Alive ONLY via memberlist NotifyJoin
               (NOT via join intent messages)
```

### Current Implementation (Scattered)

```csharp
// In HandleNodeJoinIntent
if (member.Status == MemberStatus.Leaving)
{
    member.Status = MemberStatus.Alive; // Leaving → Alive
}
else if (member.Status == MemberStatus.Left || member.Status == MemberStatus.Failed)
{
    return false; // BLOCKED transition
}

// In HandleNodeLeaveIntent  
if (member.Status == MemberStatus.Alive)
{
    member.Status = MemberStatus.Leaving; // Alive → Leaving
}
else if (member.Status == MemberStatus.Failed)
{
    member.Status = MemberStatus.Left; // Failed → Left
}

// In HandleNodeLeave (from memberlist)
if (node.State == NodeStateType.Dead)
{
    member.Status = MemberStatus.Failed; // Alive → Failed
}
else
{
    member.Status = MemberStatus.Left; // Alive/Leaving → Left
}

// In HandleNodeJoin (from memberlist)
member.Status = MemberStatus.Alive; // ANY → Alive (authoritative!)
```

**Problem:** Hard to see all valid transitions at a glance.

---

## State Machine Pattern Options

### Option 1: Explicit State Machine Class

```csharp
public interface IMemberStateMachine
{
    MemberStatus CurrentState { get; }
    
    // Transition methods with guards
    bool TryTransitionOnJoinIntent(LamportTime intentTime, LamportTime currentTime);
    bool TryTransitionOnLeaveIntent(LamportTime intentTime, LamportTime currentTime);
    void TransitionOnMemberlistJoin(); // Authoritative - always succeeds
    void TransitionOnMemberlistLeave(bool isDead);
    void TransitionOnMemberlistFailed();
    
    // Query
    bool CanTransitionTo(MemberStatus newState, TransitionTrigger trigger);
}

public enum TransitionTrigger
{
    JoinIntent,
    LeaveIntent,
    MemberlistJoin,     // Authoritative
    MemberlistLeave,
    MemberlistFailed,
    RemoveFailedNode
}

public class MemberStateMachine : IMemberStateMachine
{
    private MemberStatus _state;
    private LamportTime _statusLTime;
    
    public MemberStatus CurrentState => _state;
    
    public bool TryTransitionOnJoinIntent(LamportTime intentTime, LamportTime currentTime)
    {
        // Guard: Lamport time check
        if (intentTime <= currentTime)
            return false;
        
        // Guard: Critical auto-rejoin logic
        if (_state == MemberStatus.Left || _state == MemberStatus.Failed)
            return false; // BLOCKED - must use memberlist join
        
        // Valid transition: Leaving → Alive
        if (_state == MemberStatus.Leaving)
        {
            _state = MemberStatus.Alive;
            _statusLTime = intentTime;
            return true;
        }
        
        return false;
    }
    
    public bool TryTransitionOnLeaveIntent(LamportTime intentTime, LamportTime currentTime)
    {
        if (intentTime <= currentTime)
            return false;
        
        switch (_state)
        {
            case MemberStatus.Alive:
                _state = MemberStatus.Leaving;
                _statusLTime = intentTime;
                return true;
                
            case MemberStatus.Failed:
                _state = MemberStatus.Left;
                _statusLTime = intentTime;
                return true;
                
            case MemberStatus.Left:
                return false; // Already left
                
            default:
                return false;
        }
    }
    
    public void TransitionOnMemberlistJoin()
    {
        // Authoritative - always succeeds, any state → Alive
        _state = MemberStatus.Alive;
    }
    
    public void TransitionOnMemberlistLeave(bool isDead)
    {
        if (isDead)
        {
            _state = MemberStatus.Failed;
        }
        else
        {
            _state = MemberStatus.Left;
        }
    }
}
```

**Benefits:**
- ✅ All transitions in one place
- ✅ Guards are explicit and clear
- ✅ Easy to add new transitions
- ✅ Testable in isolation
- ✅ Self-documenting

**Usage in IntentHandler:**
```csharp
public bool HandleJoinIntent(MessageJoin join)
{
    _clock.Witness(join.LTime);
    
    return _memberManager.ExecuteUnderLock(accessor =>
    {
        var member = accessor.GetMember(join.Node);
        
        if (member == null)
        {
            // Create new member
            var newMember = new MemberInfo
            {
                Name = join.Node,
                StateMachine = new MemberStateMachine(MemberStatus.Alive, join.LTime)
            };
            accessor.AddMember(newMember);
            return true;
        }
        
        // Use state machine for transition
        var transitioned = member.StateMachine.TryTransitionOnJoinIntent(
            join.LTime, 
            member.StatusLTime);
        
        if (transitioned)
        {
            member.StatusLTime = join.LTime;
            return true;
        }
        
        return false;
    });
}
```

---

### Option 2: State Pattern (GoF)

```csharp
public interface IMemberState
{
    MemberStatus Status { get; }
    
    IMemberState OnJoinIntent(LamportTime intentTime, LamportTime currentTime);
    IMemberState OnLeaveIntent(LamportTime intentTime, LamportTime currentTime);
    IMemberState OnMemberlistJoin();
    IMemberState OnMemberlistLeave(bool isDead);
}

public class AliveState : IMemberState
{
    public MemberStatus Status => MemberStatus.Alive;
    
    public IMemberState OnJoinIntent(LamportTime intentTime, LamportTime currentTime)
    {
        // Already alive, no transition
        return this;
    }
    
    public IMemberState OnLeaveIntent(LamportTime intentTime, LamportTime currentTime)
    {
        if (intentTime <= currentTime)
            return this;
        
        return new LeavingState();
    }
    
    public IMemberState OnMemberlistJoin()
    {
        return this; // Already alive
    }
    
    public IMemberState OnMemberlistLeave(bool isDead)
    {
        return isDead ? new FailedState() : new LeftState();
    }
}

public class LeavingState : IMemberState
{
    public MemberStatus Status => MemberStatus.Leaving;
    
    public IMemberState OnJoinIntent(LamportTime intentTime, LamportTime currentTime)
    {
        if (intentTime <= currentTime)
            return this;
        
        // Valid transition: Leaving → Alive (refutation)
        return new AliveState();
    }
    
    public IMemberState OnLeaveIntent(LamportTime intentTime, LamportTime currentTime)
    {
        return this; // Already leaving
    }
    
    public IMemberState OnMemberlistJoin()
    {
        return new AliveState();
    }
    
    public IMemberState OnMemberlistLeave(bool isDead)
    {
        return new LeftState();
    }
}

public class LeftState : IMemberState
{
    public MemberStatus Status => MemberStatus.Left;
    
    public IMemberState OnJoinIntent(LamportTime intentTime, LamportTime currentTime)
    {
        // CRITICAL: Cannot resurrect via join intent
        return this;
    }
    
    public IMemberState OnLeaveIntent(LamportTime intentTime, LamportTime currentTime)
    {
        return this; // Already left
    }
    
    public IMemberState OnMemberlistJoin()
    {
        // Authoritative - can resurrect
        return new AliveState();
    }
    
    public IMemberState OnMemberlistLeave(bool isDead)
    {
        return this; // Already left
    }
}

public class FailedState : IMemberState
{
    public MemberStatus Status => MemberStatus.Failed;
    
    public IMemberState OnJoinIntent(LamportTime intentTime, LamportTime currentTime)
    {
        // CRITICAL: Cannot resurrect via join intent
        return this;
    }
    
    public IMemberState OnLeaveIntent(LamportTime intentTime, LamportTime currentTime)
    {
        if (intentTime <= currentTime)
            return this;
        
        // Failed → Left (via RemoveFailedNode)
        return new LeftState();
    }
    
    public IMemberState OnMemberlistJoin()
    {
        // Authoritative - can resurrect
        return new AliveState();
    }
    
    public IMemberState OnMemberlistLeave(bool isDead)
    {
        return this; // Already failed
    }
}
```

**Benefits:**
- ✅ Each state is a separate class (SRP)
- ✅ Easy to add new states
- ✅ Transition logic encapsulated in state classes
- ✅ Very OOP

**Drawbacks:**
- ⚠️ More classes to manage
- ⚠️ Harder to see all transitions (spread across classes)
- ⚠️ More complex for this use case

---

### Option 3: State Machine Library (Stateless, etc.)

```csharp
// Using Stateless library
public class MemberStateMachine
{
    private readonly StateMachine<MemberStatus, TransitionTrigger> _machine;
    
    public MemberStateMachine(MemberStatus initialState)
    {
        _machine = new StateMachine<MemberStatus, TransitionTrigger>(initialState);
        
        _machine.Configure(MemberStatus.Alive)
            .Permit(TransitionTrigger.LeaveIntent, MemberStatus.Leaving)
            .Permit(TransitionTrigger.MemberlistFailed, MemberStatus.Failed)
            .Permit(TransitionTrigger.MemberlistLeave, MemberStatus.Left);
        
        _machine.Configure(MemberStatus.Leaving)
            .Permit(TransitionTrigger.JoinIntent, MemberStatus.Alive)  // Refutation
            .Permit(TransitionTrigger.LeaveIntent, MemberStatus.Left)
            .Permit(TransitionTrigger.MemberlistJoin, MemberStatus.Alive);
        
        _machine.Configure(MemberStatus.Left)
            .Ignore(TransitionTrigger.JoinIntent)  // CRITICAL: Cannot resurrect
            .Permit(TransitionTrigger.MemberlistJoin, MemberStatus.Alive);  // Authoritative
        
        _machine.Configure(MemberStatus.Failed)
            .Ignore(TransitionTrigger.JoinIntent)  // CRITICAL: Cannot resurrect
            .Permit(TransitionTrigger.LeaveIntent, MemberStatus.Left)
            .Permit(TransitionTrigger.MemberlistJoin, MemberStatus.Alive);  // Authoritative
    }
    
    public MemberStatus CurrentState => _machine.State;
    
    public bool CanFire(TransitionTrigger trigger) => _machine.CanFire(trigger);
    
    public void Fire(TransitionTrigger trigger) => _machine.Fire(trigger);
}
```

**Benefits:**
- ✅ Declarative configuration
- ✅ Built-in guards and validation
- ✅ Can generate diagrams
- ✅ Well-tested library

**Drawbacks:**
- ⚠️ External dependency
- ⚠️ Still need guard logic for Lamport times

---

## Comparison Matrix

| Aspect | Current (Scattered) | Explicit State Machine | State Pattern | Stateless Library |
|--------|-------------------|----------------------|---------------|-------------------|
| Clarity of transitions | ❌ Low | ✅ High | 🟡 Medium | ✅ High |
| Easy to add transitions | ❌ Hard | ✅ Easy | ✅ Easy | ✅ Easy |
| Testability | 🟡 Medium | ✅ High | ✅ High | ✅ High |
| Code organization | ❌ Poor | ✅ Good | 🟡 Medium | ✅ Good |
| Complexity | 🟢 Low | 🟢 Low | 🟡 Medium | 🟢 Low |
| Dependencies | ✅ None | ✅ None | ✅ None | ⚠️ External |
| Guard logic (Lamport) | 🟡 Mixed | ✅ Clear | 🟡 Split | 🟡 Separate |
| Critical logic visibility | ❌ Hidden | ✅ Explicit | 🟡 Split | ✅ Explicit |

---

## Recommendation: Explicit State Machine Class

**Why:**

1. **Clarity:** All transitions in one place
2. **Guards:** Lamport time checks + status checks together
3. **Critical Logic:** Left/Failed resurrection blocking is obvious
4. **Testability:** Can test state machine independently
5. **No Dependencies:** Pure C# implementation
6. **Documentation:** Self-documenting code

**Example Implementation:**

```csharp
public class MemberInfo
{
    public string Name { get; set; }
    public Member? Member { get; set; }
    
    // Replace status + statusLTime with state machine
    public MemberStateMachine StateMachine { get; set; }
    
    // Convenience properties
    public MemberStatus Status => StateMachine.CurrentState;
    public LamportTime StatusLTime => StateMachine.StatusLTime;
}

public class MemberStateMachine
{
    private MemberStatus _state;
    private LamportTime _statusLTime;
    
    public MemberStatus CurrentState => _state;
    public LamportTime StatusLTime => _statusLTime;
    
    public MemberStateMachine(MemberStatus initialState, LamportTime initialTime)
    {
        _state = initialState;
        _statusLTime = initialTime;
    }
    
    public TransitionResult TryTransitionOnJoinIntent(LamportTime intentTime)
    {
        // Guard: Lamport time
        if (intentTime <= _statusLTime)
            return TransitionResult.Rejected("Stale intent (Lamport time)");
        
        // Guard: CRITICAL auto-rejoin logic
        if (_state == MemberStatus.Left)
            return TransitionResult.Rejected("Cannot resurrect Left member via join intent");
        
        if (_state == MemberStatus.Failed)
            return TransitionResult.Rejected("Cannot resurrect Failed member via join intent");
        
        // Valid transition: Leaving → Alive
        if (_state == MemberStatus.Leaving)
        {
            var oldState = _state;
            _state = MemberStatus.Alive;
            _statusLTime = intentTime;
            return TransitionResult.Success(oldState, _state, "Refutation");
        }
        
        // No transition needed (already Alive, etc.)
        return TransitionResult.NoChange("Already in valid state");
    }
    
    public TransitionResult TryTransitionOnLeaveIntent(LamportTime intentTime)
    {
        if (intentTime <= _statusLTime)
            return TransitionResult.Rejected("Stale intent");
        
        switch (_state)
        {
            case MemberStatus.Alive:
                _state = MemberStatus.Leaving;
                _statusLTime = intentTime;
                return TransitionResult.Success(MemberStatus.Alive, MemberStatus.Leaving, "Graceful leave");
                
            case MemberStatus.Failed:
                _state = MemberStatus.Left;
                _statusLTime = intentTime;
                return TransitionResult.Success(MemberStatus.Failed, MemberStatus.Left, "RemoveFailedNode");
                
            case MemberStatus.Left:
                return TransitionResult.NoChange("Already left");
                
            default:
                return TransitionResult.NoChange("No valid transition");
        }
    }
    
    public TransitionResult TransitionOnMemberlistJoin()
    {
        // Authoritative - ALWAYS succeeds
        var oldState = _state;
        _state = MemberStatus.Alive;
        return TransitionResult.Success(oldState, MemberStatus.Alive, "Memberlist authoritative join");
    }
    
    public TransitionResult TransitionOnMemberlistLeave(bool isDead)
    {
        var oldState = _state;
        _state = isDead ? MemberStatus.Failed : MemberStatus.Left;
        return TransitionResult.Success(oldState, _state, isDead ? "Detected failure" : "Graceful leave");
    }
}

public class TransitionResult
{
    public bool WasTransitioned { get; }
    public MemberStatus? OldState { get; }
    public MemberStatus? NewState { get; }
    public string Reason { get; }
    
    public static TransitionResult Success(MemberStatus oldState, MemberStatus newState, string reason)
        => new TransitionResult(true, oldState, newState, reason);
    
    public static TransitionResult Rejected(string reason)
        => new TransitionResult(false, null, null, reason);
    
    public static TransitionResult NoChange(string reason)
        => new TransitionResult(false, null, null, reason);
}
```

---

## Benefits in Practice

### Before (Scattered Logic)
```csharp
// Hard to see what's happening
if (member.Status == MemberStatus.Left || member.Status == MemberStatus.Failed)
{
    return false; // Why? What rule is this?
}
```

### After (State Machine)
```csharp
var result = member.StateMachine.TryTransitionOnJoinIntent(join.LTime);

if (!result.WasTransitioned)
{
    _logger?.LogDebug("Join intent rejected: {Reason}", result.Reason);
    // Reason: "Cannot resurrect Left member via join intent"
    return false;
}

_logger?.LogInformation("State transition: {Old} → {New} ({Reason})",
    result.OldState, result.NewState, result.Reason);
```

**Much clearer!**

---

## Integration with Transaction Pattern

```csharp
public bool HandleJoinIntent(MessageJoin join)
{
    _clock.Witness(join.LTime);
    
    return _memberManager.ExecuteUnderLock(accessor =>
    {
        var member = accessor.GetMember(join.Node);
        
        if (member == null)
        {
            var newMember = new MemberInfo
            {
                Name = join.Node,
                StateMachine = new MemberStateMachine(MemberStatus.Alive, join.LTime)
            };
            accessor.AddMember(newMember);
            return true;
        }
        
        // State machine handles ALL transition logic
        var result = member.StateMachine.TryTransitionOnJoinIntent(join.LTime);
        
        if (result.WasTransitioned)
        {
            _logger?.LogInformation(
                "Member {Name}: {Old} → {New} ({Reason})",
                join.Node, result.OldState, result.NewState, result.Reason);
            return true;
        }
        
        return false;
    });
}
```

**Perfect separation:**
- Transaction pattern ensures atomicity
- State machine ensures valid transitions
- Both concerns are properly encapsulated

---

## Testing Benefits

```csharp
[TestMethod]
public void StateMachine_RejectsLeftToAliveViaJoinIntent()
{
    var sm = new MemberStateMachine(MemberStatus.Left, 100);
    
    var result = sm.TryTransitionOnJoinIntent(200); // Newer time
    
    Assert.IsFalse(result.WasTransitioned);
    Assert.AreEqual("Cannot resurrect Left member via join intent", result.Reason);
    Assert.AreEqual(MemberStatus.Left, sm.CurrentState); // State unchanged
}

[TestMethod]
public void StateMachine_AllowsLeavingToAliveViaJoinIntent()
{
    var sm = new MemberStateMachine(MemberStatus.Leaving, 100);
    
    var result = sm.TryTransitionOnJoinIntent(200);
    
    Assert.IsTrue(result.WasTransitioned);
    Assert.AreEqual(MemberStatus.Leaving, result.OldState);
    Assert.AreEqual(MemberStatus.Alive, result.NewState);
    Assert.AreEqual(MemberStatus.Alive, sm.CurrentState);
}

[TestMethod]
public void StateMachine_AllowsAnyToAliveViaMemberlistJoin()
{
    var sm = new MemberStateMachine(MemberStatus.Left, 100);
    
    var result = sm.TransitionOnMemberlistJoin();
    
    Assert.IsTrue(result.WasTransitioned);
    Assert.AreEqual(MemberStatus.Alive, sm.CurrentState);
    Assert.AreEqual("Memberlist authoritative join", result.Reason);
}
```

**Can test state machine in complete isolation!**

---

## Discussion Questions

1. **Should we use State Machine pattern?**
   - My recommendation: YES - makes system much clearer

2. **Which variant?**
   - Explicit State Machine Class (recommended)
   - State Pattern (more OOP but more complex)
   - Stateless library (external dependency)

3. **Where to place state machine?**
   - Inside MemberInfo (my recommendation)
   - Separate service
   - Part of MemberManager

4. **How does this fit with transaction pattern?**
   - Perfect complement!
   - Transaction ensures atomicity
   - State machine ensures valid transitions

5. **What about Go's implementation?**
   - Go doesn't use explicit state machine (just if/else)
   - But logic is the same - we're just making it explicit
   - This is a C# enhancement (better structure)

---

## Next Steps

If we agree on state machine pattern:

1. Add to transformation plan
2. Implement MemberStateMachine class
3. Integrate with MemberInfo
4. Update IntentHandler to use state machine
5. Update NodeEventHandler to use state machine
6. Add comprehensive state machine tests

**Estimated effort:** +1 week (but worth it for clarity)

---

**Question for you:** Should we adopt the state machine pattern, and if so, which variant do you prefer?
