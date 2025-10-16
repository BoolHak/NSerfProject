# Missing Implementations Tracker

This document tracks placeholder interfaces and incomplete implementations that will be filled in during later phases.

## Status: ✅ All Tracked - No Blockers

All missing implementations are intentional placeholders for future phases. Current phases (1-4.1) are **COMPLETE**.

---

## Interfaces (Defined but Not Fully Implemented)

### 1. IMergeDelegate (Config.cs)
**Location**: `NSerf.Serf.Config.cs` (line 330)  
**Status**: ✅ Interface defined with proper signature  
**Planned Implementation**: Phase 6+ (Delegates & Core Serf)  
**Go Source**: `serf/merge_delegate.go`

```csharp
public interface IMergeDelegate
{
    /// <summary>
    /// NotifyMerge is invoked when members are discovered during a join operation.
    /// </summary>
    Task<string?> NotifyMerge(Member[] members);
}
```

**Will be implemented as**:
- Internal `MergeDelegate` class in core Serf
- Wraps memberlist merge notifications
- Converts memberlist nodes to Serf members
- Invokes user-provided merge delegate

---

### 2. IReconnectTimeoutOverrider (Config.cs)
**Location**: `NSerf.Serf.Config.cs` (line 344)  
**Status**: ✅ Interface defined with proper signature  
**Planned Implementation**: Phase 6+ (Core Serf - Reaper functionality)  
**Go Source**: `serf/serf.go` (line 55-59)

```csharp
public interface IReconnectTimeoutOverrider
{
    /// <summary>
    /// ReconnectTimeout is called to get the reconnect timeout for a specific member.
    /// </summary>
    TimeSpan ReconnectTimeout(Member member, TimeSpan timeout);
}
```

**Will be used in**:
- `Serf.reap()` method
- Allows per-member reconnect timeout customization
- Called before reaping failed members

---

## Event Methods (Partially Implemented)

### 3. Query.Respond() Method
**Location**: `NSerf.Serf.Events.Query.cs`  
**Status**: ⚠️ Not yet implemented (placeholder in interface)  
**Planned Implementation**: Phase 7+ (Query Handling)  
**Go Source**: `serf/query.go`

**Signature from Go**:
```go
func (q *Query) Respond(buf []byte) error
```

**Expected C# implementation**:
```csharp
public class Query : Event
{
    // ... existing fields ...
    
    /// <summary>
    /// Respond is used to send a response to the query
    /// </summary>
    public Task<string?> Respond(byte[] payload)
    {
        // Will be implemented when query handling is ported
        // - Validates query is not expired
        // - Sends response through serf's query manager
        // - Returns error if query is closed or expired
        throw new NotImplementedException("Query response handling in Phase 7+");
    }
}
```

---

## Implementation Dependencies

### Current Phases (COMPLETE)
- ✅ Phase 1: Foundation (Member, LamportTime, MessageType, etc.)
- ✅ Phase 2.1: Message Protocol
- ✅ Phase 3.1: Event Infrastructure
- ✅ Phase 3.2: Event Coalescing  
- ✅ Phase 4.1: Configuration System

### Future Phases (Where Missing Items Will Be Implemented)

**Phase 6: Delegates** (merge_delegate.go, conflict_delegate.go, etc.)
- Implement `IMergeDelegate` concrete class
- Implement internal `MergeDelegate` wrapper
- Implement `ConflictDelegate`
- Implement main `Delegate` class

**Phase 7: Core Serf** (serf.go - main Serf struct)
- Implement `Serf` class
- Implement reaper functionality (uses `IReconnectTimeoutOverrider`)
- Implement query handling (`Query.Respond()`)
- Implement join/leave logic
- Implement state management

**Phase 8: Query System** (query.go, internal_query.go)
- Full query lifecycle management
- Query response handling
- Query timeouts and filtering

---

## No Action Required

**All missing implementations are intentional placeholders.**

- Interfaces are properly defined with correct signatures
- Documentation clearly states when they'll be implemented
- No circular dependencies or blockers
- Current test suite (474 tests) validates all completed phases

**Next Step**: Continue with next planned phase as per roadmap.

---

## Validation Checklist

- [x] All interfaces have proper XML documentation
- [x] All interfaces match Go signatures (adapted for C# idioms)
- [x] Phase planning documents when each will be implemented
- [x] No compilation errors or warnings
- [x] All current tests passing (474/474)
- [x] Roadmap updated with implementation phases

**Last Updated**: Phase 4.1 Complete (Oct 16, 2025)
