# Snapshotter Fix & Test Checklist

## Section 1: Code Corrections

### Fix 1: Async Disposal Pattern ✅ DONE
**File**: `Snapshotter.cs`

- [x] Add `IAsyncDisposable` interface to class
- [x] Implement `DisposeAsync()` that:
  - Completes `_streamCh.Writer`
  - Awaits `_teeTask` and `_streamTask`
  - Calls `Dispose()`
- [x] Keep existing `Dispose()` for sync scenarios

**Key Code**:
```csharp
public async ValueTask DisposeAsync()
{
    _streamCh.Writer.Complete();
    await Task.WhenAll(_teeTask, _streamTask);
    Dispose();
}
```

---

### Fix 2: Shutdown Coordination
**File**: `Snapshotter.cs`

- [ ] Update `TeeStreamAsync()`:
  - Use `ReadAllAsync(_shutdownToken)`
  - Call `_streamCh.Writer.Complete()` in finally block
- [ ] Update `PerformShutdownFlushAsync()`:
  - Use `_streamCh.Reader.ReadAllAsync()` with timeout
  - Remove `TryRead` loop

**Key Change**: TeeStream signals completion before StreamAsync drains

---

### Fix 3: Bounded Channels ✅ DONE
**File**: `Snapshotter.cs`

- [x] Line 80: Change `CreateUnbounded` to `CreateBounded` with capacity 2048
- [x] Line 166: Same change for `_streamCh`
- [x] Set `FullMode = BoundedChannelFullMode.Wait`

**Prevents**: Unbounded memory growth

---

### Fix 4: Async I/O
**File**: `Snapshotter.cs`

- [ ] Convert `ForceFlush()` to `ForceFlushAsync()`
- [ ] Convert `ProcessMemberEvent()` to `ProcessMemberEventAsync()`
- [ ] Convert `FlushEvent()` to `FlushEventAsync()`
- [ ] Update all callers to await
- [ ] Use `FlushAsync()` instead of `Flush()` where safe

**Goal**: No blocking I/O in async methods

---

### Fix 5: Compaction Lock Optimization
**File**: `Snapshotter.cs`

- [ ] Move file write operations outside `_fileLock`
- [ ] Only hold lock during file swap
- [ ] Snapshot `_aliveNodes` with short lock, write without lock

**Benefit**: Lock held <10ms instead of 100ms+

---

### Fix 6: Leave Synchronization
**File**: `Snapshotter.cs`

- [ ] Update `LeaveAsync()` to wait for flush confirmation
- [ ] Add delay or completion signal after writing leave marker

---

## Section 2: Unit Tests

### Test Group A: Disposal & Lifecycle

#### A1: DisposeAsync Waits for Tasks ✅ PASSING
```csharp
[Fact]
public async Task DisposeAsync_ShouldWaitForTaskCompletion()
{
    var (inCh, snap) = await CreateSnapshotter();
    shutdownCts.Cancel();
    await snap.DisposeAsync();
    Assert.True(snap._teeTask.IsCompleted);
    Assert.True(snap._streamTask.IsCompleted);
}
```
**Verifies**: Tasks complete before disposal returns

---

#### A2: Dispose Doesn't Hang ✅ PASSING
```csharp
[Fact]
public void Dispose_ShouldNotHang()
{
    var snap = CreateSnapshotter().Result.Snap;
    shutdownCts.Cancel();
    snap.Dispose();  // Should return immediately
}
```
**Verifies**: Sync disposal works

---

### Test Group B: Shutdown Coordination

#### B1: No Events Lost During Shutdown
```csharp
[Fact]
public async Task Shutdown_ShouldNotLoseEvents()
{
    // Send 100 events
    // Shutdown immediately
    // Verify all 100 written to file
}
```
**Verifies**: All events drained before exit

---

#### B2: Shutdown Respects Timeout
```csharp
[Fact]
public async Task Shutdown_ShouldCompleteWithinTimeout()
{
    // Queue 10k events
    // Shutdown
    // Assert completes in < 1 second
}
```
**Verifies**: Timeout prevents infinite wait

---

### Test Group C: Channel Backpressure

#### C1: Bounded Channel Blocks When Full ✅ PASSING
```csharp
[Fact]
public async Task BoundedChannel_ShouldApplyBackpressure()
{
    // Flood channel with 5000 events
    // Assert write task still running (blocked)
}
```
**Verifies**: Backpressure applied

---

#### C2: Memory Stays Bounded Under Load ✅ PASSING
```csharp
[Fact]
public async Task MemoryUsage_ShouldStayBounded()
{
    // Send 1000 events
    // Assert memory growth < 10MB
}
```
**Verifies**: No memory leaks

---

### Test Group D: Compaction

#### D1: Compaction With Concurrent Writes
```csharp
[Fact]
public async Task Compaction_WithConcurrentWrites_ShouldNotCorrupt()
{
    // Write continuously to trigger compaction
    // Verify snapshot valid after compaction
}
```
**Verifies**: No corruption during compaction

---

#### D2: Compaction Lock Time
```csharp
[Fact]
public async Task Compaction_ShouldHoldLockBriefly()
{
    // Trigger compaction
    // Measure write latencies during compaction
    // Assert all < 50ms
}
```
**Verifies**: Short lock duration

---

### Test Group E: Leave Handling

#### E1: Leave Flushes Before Return
```csharp
[Fact]
public async Task Leave_ShouldFlushBeforeReturning()
{
    await snap.LeaveAsync();
    // Assert "leave" marker in file immediately
}
```
**Verifies**: Synchronous flush

---

#### E2: RejoinAfterLeave Preserves State
```csharp
[Fact]
public async Task Leave_WithRejoinAfterLeave_ShouldPreserveNodes()
{
    // Create with rejoinAfterLeave=true
    // Add member, leave
    // Assert nodes still in memory
}
```
**Verifies**: Correct rejoin behavior

---

### Test Group F: Recovery

#### F1: Replay Recovers State
```csharp
[Fact]
public async Task Replay_ShouldRecoverState()
{
    // Write 5 members, shutdown
    // Create new snapshotter with same file
    // Assert 5 members recovered
}
```
**Verifies**: State persistence

---

#### F2: Replay With Leave Clears State
```csharp
[Fact]
public async Task Replay_WithLeave_ShouldClearState()
{
    // Create with rejoinAfterLeave=false
    // Add member, leave, shutdown
    // Replay - assert no members
}
```
**Verifies**: Leave marker respected

---

## Execution Plan

### Phase 1: Critical Fixes (Day 1-2)
1. Fix 2: Shutdown coordination
2. Fix 1: Async disposal
3. Tests B1, B2, A1, A2

### Phase 2: Performance (Day 3-4)
4. Fix 3: Bounded channels
5. Fix 4: Async I/O
6. Tests C1, C2

### Phase 3: Reliability (Day 5)
7. Fix 5: Compaction optimization
8. Fix 6: Leave sync
9. Tests D1, D2, E1, E2, F1, F2

### Phase 4: Integration (Day 6)
10. Run all existing tests
11. Fix regressions
12. Update Serf.cs to use DisposeAsync

---

## Success Criteria

- [ ] All 12 new tests pass
- [ ] All existing tests still pass
- [ ] No memory leaks (run for 10 minutes under load)
- [ ] Shutdown < 1 second with 1000 pending events
- [ ] Compaction lock < 10ms
- [ ] No data loss during crash recovery

---

## Testing Commands

```bash
# Run new tests
dotnet test --filter "Category=Snapshotter"

# Run all tests
dotnet test

