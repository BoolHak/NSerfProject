# Serf Refactoring - TDD Implementation Checklist
**Branch:** `feature/refactor-serf-class`  
**Start Date:** October 20, 2025  
**Methodology:** Test-Driven Development (TDD)

---

## 📋 Pre-Implementation

### Setup & Review
- [x] Create feature branch `feature/refactor-serf-class`
- [ ] Review all refactoring documents:
  - [ ] SERF_TRANSFORMATION_MASTER_PLAN.md
  - [ ] COMPLETE_STATE_MACHINE_DESIGN.md
  - [ ] STATE_MACHINE_DISCUSSION.md
  - [ ] CODE_REFACTORING_REPORT.md
- [ ] Understand TDD workflow: RED → GREEN → REFACTOR

---

## Phase 1: State Machine Foundation (Week 1)

### 🔴 TDD Step 1: Write Tests FIRST (RED)

#### 1.1 Create Test Project Structure
- [x] Create directory: `NSerfTests/Serf/StateMachine/`
- [x] Create `MemberStateMachineTests.cs`

#### 1.2 Write Test: TransitionResult Behavior
- [x] Test: `TransitionResult_StateChanged_HasCorrectProperties`
- [x] Test: `TransitionResult_LTimeUpdated_HasCorrectProperties`
- [x] Test: `TransitionResult_Rejected_HasCorrectProperties`
- [x] Test: `TransitionResult_NoChange_HasCorrectProperties`

#### 1.3 Write Test: Stale Message Rejection
- [x] Test: `JoinIntent_WithStaleLTime_IsRejected`
- [x] Test: `LeaveIntent_WithStaleLTime_IsRejected`
- [x] Test: `JoinIntent_WithEqualLTime_IsRejected`
- [x] Test: `LeaveIntent_WithEqualLTime_IsRejected`

#### 1.4 Write Test: CRITICAL Auto-Rejoin Logic ⚠️
- [x] Test: `JoinIntent_LeftMember_BlocksStateChange_ButUpdatesLTime`
- [x] Test: `JoinIntent_FailedMember_BlocksStateChange_ButUpdatesLTime`

#### 1.5 Write Test: Valid Refutation (Leaving → Alive)
- [x] Test: `JoinIntent_LeavingMember_TransitionsToAlive`
- [x] Test: `JoinIntent_AliveMember_UpdatesLTimeOnly`

#### 1.6 Write Test: Leave Intent Transitions
- [x] Test: `LeaveIntent_AliveMember_TransitionsToLeaving`
- [x] Test: `LeaveIntent_FailedMember_TransitionsToLeft`
- [x] Test: `LeaveIntent_LeftMember_UpdatesLTimeOnly`
- [x] Test: `LeaveIntent_LeavingMember_UpdatesLTimeOnly`

#### 1.7 Write Test: AUTHORITATIVE Memberlist Transitions
- [x] Test: `MemberlistJoin_FromLeft_AlwaysSucceeds`
- [x] Test: `MemberlistJoin_FromFailed_AlwaysSucceeds`
- [x] Test: `MemberlistJoin_FromLeaving_AlwaysSucceeds`
- [x] Test: `MemberlistJoin_FromAlive_NoChange`
- [x] Test: `MemberlistLeave_Dead_TransitionsToFailed`
- [x] Test: `MemberlistLeave_Graceful_TransitionsToLeft`

#### 1.8 Write Test: Edge Cases
- [x] Test: `LeaveComplete_FromLeaving_TransitionsToLeft`
- [x] Test: `LeaveComplete_FromNonLeaving_NoChange`
- [x] Test: `MultipleTransitions_MaintainCorrectLTime`
- [x] Test: `Constructor_SetsInitialStateAndTime`

**Total Tests Written:** 25 tests ✅  
**Status:** Tests written (using xUnit) - Ready for GREEN phase

---

### 🟢 TDD Step 2: Implement to Make Tests PASS (GREEN)

#### 2.1 Create Production Code Structure
- [x] Create directory: `NSerf/NSerf/Serf/StateMachine/`
- [x] Verify `MemberStatus.cs` already exists
  - [x] Verified enum values: None=0, Alive=1, Leaving=2, Left=3, Failed=4
  - [x] XML documentation present

#### 2.2 Implement TransitionResult (Make tests pass)
- [x] Create `TransitionResult.cs`
- [x] Implement ResultType enum (StateChanged, LTimeUpdated, NoChange, Rejected)
- [x] Implement properties (Type, OldState, NewState, NewLTime, Reason)
- [x] Implement static factory methods
- [x] Implement helper properties (WasStateChanged, WasLTimeUpdated, WasRejected)

#### 2.3 Implement MemberStateMachine (Make tests pass)
- [x] Create `MemberStateMachine.cs`
- [x] Implement constructor
- [x] Implement properties (CurrentState, StatusLTime)
- [x] Implement `TryTransitionOnJoinIntent`
  - [x] Guard: Reject if LTime <= StatusLTime
  - [x] Update StatusLTime regardless
  - [x] CRITICAL: Block Left/Failed, return LTimeUpdated
  - [x] Allow Leaving → Alive, return StateChanged
  - [x] Handle other cases
- [x] Implement `TryTransitionOnLeaveIntent`
  - [x] Guard: Reject if LTime <= StatusLTime
  - [x] Update StatusLTime
  - [x] Alive → Leaving
  - [x] Failed → Left
  - [x] Handle other cases
- [x] Implement `TransitionOnMemberlistJoin`
  - [x] AUTHORITATIVE - always succeeds
  - [x] Any state → Alive
- [x] Implement `TransitionOnMemberlistLeave`
  - [x] AUTHORITATIVE - always succeeds
  - [x] isDead → Failed
  - [x] !isDead → Left
- [x] Implement `TransitionOnLeaveComplete`
  - [x] Only from Leaving → Left

**Status:** Implementation complete ✅  
**Tests:** All 26 tests PASSING ✅

---

### 🔵 TDD Step 3: Integrate State Machine (REFACTOR)

#### 3.1 Add StateMachine to MemberInfo
- [x] Update `MemberStateMachine` to use `LamportTime` instead of `ulong`
- [x] Update `TransitionResult` to use `LamportTime` instead of `ulong?`
- [x] Open `MemberInfo` in `Serf.cs`
- [x] Add property: `public MemberStateMachine? StateMachine { get; set; }`
- [x] Add convenience properties with backward compatibility:
  - [x] `public MemberStatus Status` (delegates to StateMachine if present)
  - [x] `public LamportTime StatusLTime` (delegates to StateMachine if present)
- [x] Keep backing fields for backward compatibility:
  - [x] `private MemberStatus _status`
  - [x] `private LamportTime _statusLTime`

#### 3.2 Verify Integration
- [x] Compile - no errors ✅
- [x] Run all state machine tests - **26/26 PASSING** ✅
- [x] Run existing Serf tests - **810/815 PASSING** ✅
  - 5 failures are pre-existing (4 socket binding, 1 snapshot auto-rejoin)
  - **NO NEW FAILURES** - state machine integration is clean! ✅

**Status:** State Machine integrated with `LamportTime` support - All tests PASSING ✅

**Phase 1 Complete: State Machine with TDD** ✅

---

## Phase 2: Member Manager with Transaction Pattern (Week 2)

### 🔴 TDD Step 1: Write Tests FIRST (RED)

#### 2.1 Create Test Structure
- [ ] Create directory: `NSerf/NSerfTests/Serf/Managers/`
- [ ] Create `MemberManagerTests.cs`

#### 2.2 Write Test: Basic Query Operations
- [ ] Test: `GetMembers_ReturnsAllMembers`
  ```csharp
  var manager = CreateTestManager();
  
  manager.ExecuteUnderLock(accessor =>
  {
      accessor.AddMember(CreateTestMember("node1", MemberStatus.Alive));
      accessor.AddMember(CreateTestMember("node2", MemberStatus.Alive));
  });
  
  var members = manager.GetMembers();
  Assert.AreEqual(2, members.Length);
  ```
- [ ] Test: `GetMembers_WithFilter_ReturnsOnlyMatchingStatus`
- [ ] Test: `GetMemberCount_ReturnsCorrectCount`
- [ ] Test: `GetLocalMember_ReturnsLocalNodeInfo`
- [ ] **Expected:** All tests FAIL ❌

#### 2.3 Write Test: Transaction Pattern (Critical!)
- [ ] Test: `ExecuteUnderLock_ProvidesMemberStateAccessor`
  ```csharp
  var manager = CreateTestManager();
  
  var executed = false;
  manager.ExecuteUnderLock(accessor =>
  {
      Assert.IsNotNull(accessor);
      Assert.IsInstanceOfType(accessor, typeof(IMemberStateAccessor));
      executed = true;
  });
  
  Assert.IsTrue(executed);
  ```
- [ ] Test: `ExecuteUnderLock_ReturnsResult`
  ```csharp
  var result = manager.ExecuteUnderLock(accessor => 42);
  Assert.AreEqual(42, result);
  ```
- [ ] Test: `ExecuteUnderLock_IsAtomic`
  ```csharp
  // Add member and query within same transaction
  var result = manager.ExecuteUnderLock(accessor =>
  {
      accessor.AddMember(CreateTestMember("node1", MemberStatus.Alive));
      var member = accessor.GetMember("node1");
      return member != null;
  });
  
  Assert.IsTrue(result);
  ```
- [ ] Test: `ExecuteUnderLock_ThrowsException_DoesNotCorruptState`
- [ ] **Expected:** All tests FAIL ❌

#### 2.4 Write Test: Member State Accessor Operations
- [ ] Test: `Accessor_GetMember_ReturnsNull_WhenNotFound`
- [ ] Test: `Accessor_GetMember_ReturnsMember_WhenExists`
- [ ] Test: `Accessor_AddMember_AddsToCollection`
- [ ] Test: `Accessor_UpdateMember_ModifiesExistingMember`
  ```csharp
  manager.ExecuteUnderLock(accessor =>
  {
      var member = CreateTestMember("node1", MemberStatus.Alive);
      accessor.AddMember(member);
      
      accessor.UpdateMember("node1", m => {
          var result = m.StateMachine.TryTransitionOnLeaveIntent(200);
          Assert.IsTrue(result.WasStateChanged);
      });
      
      var updated = accessor.GetMember("node1");
      Assert.AreEqual(MemberStatus.Leaving, updated.Status);
  });
  ```
- [ ] Test: `Accessor_RemoveMember_RemovesFromAllCollections`
- [ ] Test: `Accessor_GetAllMembers_ReturnsReadOnlyDictionary`
- [ ] **Expected:** All tests FAIL ❌

#### 2.5 Write Test: Failed/Left Lists Management
- [ ] Test: `GetFailedMembersSnapshot_ReturnsIsolatedCopy`
- [ ] Test: `GetLeftMembersSnapshot_ReturnsIsolatedCopy`
- [ ] Test: `Accessor_FailedMembers_CanBeModified`
- [ ] Test: `Accessor_LeftMembers_CanBeModified`
- [ ] **Expected:** All tests FAIL ❌

**Total Tests Written:** ~20 tests  
**All should be RED (failing)** ❌

---

### 🟢 TDD Step 2: Implement to Make Tests PASS (GREEN)

#### 2.6 Create Interfaces
- [ ] Create directory: `NSerf/NSerf/Serf/Managers/`
- [ ] Create `IMemberManager.cs`
  - [ ] ExecuteUnderLock<TResult> signature
  - [ ] ExecuteUnderLock(Action) signature
  - [ ] GetMembers() signatures
  - [ ] GetMemberCount() signature
  - [ ] GetLocalMember() signature
  - [ ] Snapshot methods signatures
- [ ] Create `IMemberStateAccessor.cs`
  - [ ] GetMember(string) signature
  - [ ] AddMember(MemberInfo) signature
  - [ ] UpdateMember(string, Action) signature
  - [ ] RemoveMember(string) signature
  - [ ] Collection access signatures

#### 2.7 Implement MemberManager
- [ ] Create `MemberManager.cs`
- [ ] Add fields:
  - [ ] ReaderWriterLockSlim _lock
  - [ ] Dictionary<string, MemberInfo> _members
  - [ ] List<MemberInfo> _failedMembers
  - [ ] List<MemberInfo> _leftMembers
  - [ ] Dependencies (Config, LamportClock, etc.)
- [ ] Implement ExecuteUnderLock methods
  - [ ] Acquire write lock
  - [ ] Create MemberStateAccessor
  - [ ] Execute function
  - [ ] Release lock in finally
- [ ] **Verify:** Transaction pattern tests PASS ✅
- [ ] Implement query methods
  - [ ] GetMembers() with read lock
  - [ ] GetMembers(filter) with read lock
  - [ ] GetMemberCount() with read lock
- [ ] **Verify:** Query tests PASS ✅
- [ ] Implement snapshot methods
  - [ ] GetFailedMembersSnapshot() - ToList() copy
  - [ ] GetLeftMembersSnapshot() - ToList() copy
- [ ] **Verify:** Snapshot tests PASS ✅

#### 2.8 Implement MemberStateAccessor
- [ ] Create `Internal/MemberStateAccessor.cs`
- [ ] Implement GetMember (direct dictionary access)
- [ ] Implement AddMember (direct dictionary access)
- [ ] Implement UpdateMember (get + action)
- [ ] Implement RemoveMember (remove from all collections)
- [ ] Implement collection access methods
- [ ] **Verify:** Accessor tests PASS ✅

**All MemberManager Tests Should Now Pass** ✅ (GREEN)

---

### 🔵 TDD Step 3: Integrate with Serf (REFACTOR)

#### 2.9 Add Adapter Pattern to Serf
- [ ] Add field to Serf.cs: `private IMemberManager? _memberManager`
- [ ] Add feature flag: `private bool _useMemberManager = false`
- [ ] Create adapter in Members() method:
  ```csharp
  public Member[] Members()
  {
      if (_useMemberManager && _memberManager != null)
          return _memberManager.GetMembers();
      
      // Original implementation (fallback)
      return WithReadLock(_memberLock, () => ...);
  }
  ```
- [ ] Create adapters for:
  - [ ] NumMembers()
  - [ ] Members(MemberStatus filter)
  - [ ] LocalMember()
- [ ] Initialize MemberManager in CreateAsync:
  ```csharp
  if (_useMemberManager)
  {
      _memberManager = new MemberManager(config, clock, ...);
  }
  ```

#### 2.10 Validate Adapter Pattern
- [ ] Compile - no errors
- [ ] With `_useMemberManager = false`:
  - [ ] Run all existing Serf tests - should pass ✅
- [ ] With `_useMemberManager = true`:
  - [ ] Run all existing Serf tests - should pass ✅
  - [ ] Run MemberManager tests - should pass ✅
- [ ] Outputs should be identical in both modes

**Phase 2 Complete: MemberManager with TDD** ✅

---

## Phase 3: Intent Handlers (Week 3) ⚠️ CRITICAL

### 🔴 TDD Step 1: Write Tests FIRST (RED)

#### 3.1 Create Test Structure
- [ ] Create directory: `NSerf/NSerfTests/Serf/Handlers/`
- [ ] Create `IntentHandlerTests.cs`

#### 3.2 Write Test: CRITICAL Auto-Rejoin Logic ⚠️
- [ ] Test: `HandleJoinIntent_LeftMember_BlocksResurrection_UpdatesLTime`
  ```csharp
  // Setup
  var mockMemberManager = CreateMockMemberManager();
  var memberInLeftState = CreateMember("node1", MemberStatus.Left, 100);
  SetupMemberManagerToReturn(mockMemberManager, memberInLeftState);
  
  var handler = new IntentHandler(mockMemberManager, ...);
  var joinIntent = new MessageJoin { Node = "node1", LTime = 200 };
  
  // Act
  var shouldRebroadcast = handler.HandleJoinIntent(joinIntent);
  
  // Assert - CRITICAL
  Assert.IsFalse(shouldRebroadcast); // Should NOT rebroadcast
  VerifyStateUnchanged(memberInLeftState, MemberStatus.Left); // Still Left
  VerifyLTimeUpdated(memberInLeftState, 200); // LTime updated
  VerifyNoEventEmitted(); // No event
  ```
- [ ] Test: `HandleJoinIntent_FailedMember_BlocksResurrection_UpdatesLTime`
- [ ] Test: `HandleJoinIntent_LeavingMember_AllowsRefutation_EmitsEvent`
  ```csharp
  var memberInLeavingState = CreateMember("node1", MemberStatus.Leaving, 100);
  
  var shouldRebroadcast = handler.HandleJoinIntent(joinIntent);
  
  Assert.IsTrue(shouldRebroadcast); // Should rebroadcast
  VerifyStateChanged(member, MemberStatus.Leaving, MemberStatus.Alive);
  VerifyEventEmitted(EventType.MemberJoin);
  ```
- [ ] **Expected:** All tests FAIL ❌

#### 3.3 Write Test: Stale Intent Rejection
- [ ] Test: `HandleJoinIntent_StaleIntent_IsRejected`
  ```csharp
  var member = CreateMember("node1", MemberStatus.Alive, 100);
  var staleIntent = new MessageJoin { Node = "node1", LTime = 50 }; // 50 < 100
  
  var shouldRebroadcast = handler.HandleJoinIntent(staleIntent);
  
  Assert.IsFalse(shouldRebroadcast);
  VerifyStateUnchanged(member);
  ```
- [ ] Test: `HandleLeaveIntent_StaleIntent_IsRejected`
- [ ] **Expected:** All tests FAIL ❌

#### 3.4 Write Test: New Member Creation
- [ ] Test: `HandleJoinIntent_NewMember_CreatesAlive`
  ```csharp
  SetupMemberManagerToReturnNull(); // Member doesn't exist
  
  var shouldRebroadcast = handler.HandleJoinIntent(joinIntent);
  
  Assert.IsTrue(shouldRebroadcast);
  VerifyMemberCreated("node1", MemberStatus.Alive, joinIntent.LTime);
  ```
- [ ] **Expected:** All tests FAIL ❌

#### 3.5 Write Test: Leave Intent Transitions
- [ ] Test: `HandleLeaveIntent_AliveMember_TransitionsToLeaving`
- [ ] Test: `HandleLeaveIntent_FailedMember_TransitionsToLeft_MovesToLeftList`
  ```csharp
  var failedMember = CreateMember("node1", MemberStatus.Failed, 100);
  
  var shouldRebroadcast = handler.HandleLeaveIntent(leaveIntent);
  
  Assert.IsTrue(shouldRebroadcast);
  VerifyStateChanged(failedMember, MemberStatus.Failed, MemberStatus.Left);
  VerifyMovedFromFailedToLeftList(failedMember);
  VerifyEventEmitted(EventType.MemberLeave);
  ```
- [ ] **Expected:** All tests FAIL ❌

#### 3.6 Write Test: Local Node Refutation
- [ ] Test: `HandleLeaveIntent_LocalNode_RefutesWithJoinIntent`
  ```csharp
  var handler = new IntentHandler(..., localNodeName: "local");
  var leaveIntent = new MessageLeave { Node = "local", LTime = 100 };
  
  SetupLocalNodeState(SerfState.Alive);
  
  var shouldRebroadcast = handler.HandleLeaveIntent(leaveIntent);
  
  Assert.IsFalse(shouldRebroadcast);
  VerifyJoinIntentBroadcasted(newLTime: 101); // Refutation
  ```
- [ ] **Expected:** All tests FAIL ❌

**Total Tests Written:** ~15 tests  
**All should be RED (failing)** ❌

---

### 🟢 TDD Step 2: Implement to Make Tests PASS (GREEN)

#### 3.7 Create Interface
- [ ] Create directory: `NSerf/NSerf/Serf/Handlers/`
- [ ] Create `IIntentHandler.cs`
  - [ ] HandleJoinIntent(MessageJoin) signature
  - [ ] HandleLeaveIntent(MessageLeave) signature

#### 3.8 Implement IntentHandler
- [ ] Create `IntentHandler.cs`
- [ ] Add dependencies:
  - [ ] IMemberManager
  - [ ] IEventManager
  - [ ] LamportClock
  - [ ] string localNodeName
  - [ ] ILogger
- [ ] Implement HandleJoinIntent:
  - [ ] Witness Lamport time
  - [ ] Use ExecuteUnderLock for atomicity
  - [ ] Handle new member creation
  - [ ] Use StateMachine.TryTransitionOnJoinIntent
  - [ ] If WasStateChanged → emit event, return true
  - [ ] If WasLTimeUpdated (Left/Failed) → log, return false
  - [ ] If WasRejected → return false
- [ ] **Verify:** JoinIntent tests PASS ✅
- [ ] Implement HandleLeaveIntent:
  - [ ] Check if local node and alive → refute
  - [ ] Use ExecuteUnderLock
  - [ ] Use StateMachine.TryTransitionOnLeaveIntent
  - [ ] Handle Failed → Left list movement
  - [ ] Emit events
- [ ] **Verify:** LeaveIntent tests PASS ✅

**All IntentHandler Tests Should Now Pass** ✅ (GREEN)

---

### 🔵 TDD Step 3: Integrate with Serf (REFACTOR)

#### 3.9 Add Adapter Pattern
- [ ] Add field: `private IIntentHandler? _intentHandler`
- [ ] Add flag: `private bool _useIntentHandler = false`
- [ ] Create adapter in HandleNodeJoinIntent:
  ```csharp
  internal bool HandleNodeJoinIntent(MessageJoin join)
  {
      if (_useIntentHandler && _intentHandler != null)
          return _intentHandler.HandleJoinIntent(join);
      
      // Original implementation
      return OriginalHandleNodeJoinIntent(join);
  }
  ```
- [ ] Create adapter in HandleNodeLeaveIntent
- [ ] Initialize in CreateAsync

#### 3.10 Critical Validation ⚠️
- [ ] With `_useIntentHandler = false`:
  - [ ] All existing tests pass ✅
- [ ] With `_useIntentHandler = true`:
  - [ ] All existing tests pass ✅
  - [ ] IntentHandler tests pass ✅
  - [ ] Run snapshot recovery test 10 times - all pass ✅

**Phase 3 Complete: IntentHandler with TDD** ✅

---

## Phase 4: Node Event Handler (Week 4)

### 🔴 RED: Write Tests
- [ ] Create `NodeEventHandlerTests.cs`
- [ ] Test: HandleNodeJoin creates new member
- [ ] Test: HandleNodeJoin resurrects Left member (authoritative)
- [ ] Test: HandleNodeJoin resurrects Failed member (authoritative)
- [ ] Test: HandleNodeJoin cleans up failed/left lists
- [ ] Test: HandleNodeLeave (dead) transitions to Failed
- [ ] Test: HandleNodeLeave (graceful) transitions to Left
- [ ] Test: Events emitted correctly
- [ ] **Expected:** All tests FAIL ❌

### 🟢 GREEN: Implement
- [ ] Create `INodeEventHandler.cs`
- [ ] Create `NodeEventHandler.cs`
- [ ] Implement using StateMachine.TransitionOnMemberlistJoin/Leave
- [ ] Use ExecuteUnderLock for atomicity
- [ ] **Verify:** All tests PASS ✅

### 🔵 REFACTOR: Integrate
- [ ] Add adapter pattern to Serf.cs
- [ ] Validate with feature flag off/on
- [ ] **Complete** ✅

---

## Phase 5: Event Manager (Week 5)

### 🔴 RED: Write Tests
- [ ] Create `EventManagerTests.cs`
- [ ] Test: BroadcastUserEventAsync with coalescing
- [ ] Test: Event deduplication
- [ ] Test: Event buffer management
- [ ] Test: Emission to snapshotter
- [ ] Test: SetEventMinTime filtering
- [ ] **Expected:** All tests FAIL ❌

### 🟢 GREEN: Implement
- [ ] Create `IEventManager.cs` and `EventManager.cs`
- [ ] Extract event fields from Serf (_eventLock, _eventBuffer, etc.)
- [ ] Implement all event operations
- [ ] **Verify:** All tests PASS ✅

### 🔵 REFACTOR: Integrate
- [ ] Add adapter pattern to Serf.cs
- [ ] Validate with feature flag
- [ ] **Complete** ✅

---

## Phase 6: Cluster Coordinator (Week 6)

### 🔴 RED: Write Tests
- [ ] Create `ClusterCoordinatorTests.cs`
- [ ] Test: JoinAsync with multiple addresses
- [ ] Test: LeaveAsync state transitions (Alive → Leaving → Left)
- [ ] Test: ShutdownAsync prevents further operations
- [ ] Test: State validation (can't join after leave)
- [ ] **Expected:** All tests FAIL ❌

### 🟢 GREEN: Implement
- [ ] Create `IClusterCoordinator.cs` and `ClusterCoordinator.cs`
- [ ] Extract join/leave/shutdown logic
- [ ] Manage _stateLock and _joinLock
- [ ] **Verify:** All tests PASS ✅

### 🔵 REFACTOR: Integrate
- [ ] Add adapter pattern
- [ ] Validate
- [ ] **Complete** ✅

---

## Phase 7: Supporting Managers (Week 7)

### CoordinateManager (TDD)
- [ ] 🔴 Write tests for coordinate tracking
- [ ] 🟢 Implement CoordinateManager
- [ ] 🔵 Integrate

### ConflictResolver (TDD)
- [ ] 🔴 Write tests for conflict resolution
- [ ] 🟢 Implement ConflictResolver
- [ ] 🔵 Integrate

### QueryManager Enhancement (TDD)
- [ ] 🔴 Write tests for remaining query logic
- [ ] 🟢 Complete QueryManager
- [ ] 🔵 Integrate

---

## Phase 8: Cleanup & Finalization (Week 8)

### 8.1 Remove Adapter Code
- [ ] Remove all feature flags from Serf.cs
  - [ ] `_useMemberManager`
  - [ ] `_useIntentHandler`
  - [ ] `_useEventManager`
  - [ ] etc.
- [ ] Remove all original implementations (fallback code)
- [ ] Remove obsolete fields from MemberInfo
  - [ ] `[Obsolete] private MemberStatus _status`
  - [ ] `[Obsolete] private LamportTime _statusLTime`
- [ ] Clean up using statements
- [ ] Remove unused methods

### 8.2 Final Code Quality
- [ ] Verify Serf.cs is ~500 lines (down from 1,925)
- [ ] Verify no file exceeds 400 lines
- [ ] Verify all managers have clear responsibilities
- [ ] Verify all tests still pass
- [ ] Verify no compilation warnings

### 8.3 Documentation
- [ ] Update XML documentation on public APIs
- [ ] Add architecture diagram showing managers
- [ ] Document state machine transitions
- [ ] Add code examples for common operations
- [ ] Update SERF_TRANSFORMATION_MASTER_PLAN.md with "COMPLETE" status

---

## Success Criteria

### TDD Validation ✅
- All phases followed RED → GREEN → REFACTOR
- Each component has comprehensive unit tests
- Tests written BEFORE implementation
- All tests pass at end of each phase

### Architecture Goals ✅
- Serf.cs reduced from 1,925 → ~500 lines
- State machine explicitly documents all transitions
- Transaction pattern ensures atomicity
- Managers are independently testable
- Clear separation of concerns

### Critical Logic Preserved ✅
- Snapshot auto-rejoin works correctly
- Left/Failed members cannot be resurrected via intent
- Memberlist join is authoritative
- All Lamport time checks preserved
- Local node refutation works

---

## Timeline Tracking

| Phase | Week | Status |
|-------|------|--------|
| Phase 1: State Machine | 1 | ⏳ Current |
| Phase 2: MemberManager | 2 | ⏳ Pending |
| Phase 3: IntentHandler (Critical) | 3 | ⏳ Pending |
| Phase 4: NodeEventHandler | 4 | ⏳ Pending |
| Phase 5: EventManager | 5 | ⏳ Pending |
| Phase 6: ClusterCoordinator | 6 | ⏳ Pending |
| Phase 7: Supporting Managers | 7 | ⏳ Pending |
| Phase 8: Cleanup | 8 | ⏳ Pending |

**Total:** 8 weeks

---

## Notes & Issues

### Current Phase
**Phase 1** - State Machine Foundation

### Decisions Made
- Following TDD strictly (RED → GREEN → REFACTOR)
- Using adapter pattern for safe integration
- Feature flags for gradual rollout
- All tests at each phase

### Blockers
(None currently)

### Questions
(None currently)

---

**Last Updated:** October 20, 2025  
**Methodology:** Test-Driven Development  
**Status:** 🟢 Ready to start Phase 1
