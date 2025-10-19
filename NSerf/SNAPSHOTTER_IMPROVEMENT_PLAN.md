# Snapshotter Improvement Plan

## Executive Summary

This document provides a detailed analysis of potential issues in the `Snapshotter.cs` implementation and a prioritized action plan for improvements. Each issue has been verified against the Go reference implementation to ensure the improvements maintain compatibility while addressing C#-specific concerns.

---

## Issue Analysis: Real vs. False Positives

### ✅ VERIFIED REAL ISSUES

#### 1. **Race Condition in Shutdown** (CRITICAL)
**Status**: ✅ Confirmed Real Issue

**Problem**:
```csharp
// In PerformShutdownFlushAsync (line 408-413):
while (_streamCh.Reader.TryRead(out var evt))
{
    FlushEvent(evt);
}
```
- `TeeStreamAsync` continues writing to `_streamCh` during shutdown
- Events can be lost if written after `TryRead` loop completes
- No coordination signal between the two tasks

**Go Reference** (snapshot.go lines 292-310):
```go
case <-s.shutdownCh:
    // teeStream also receives shutdownCh and exits
    // THEN drains streamCh with timeout
FLUSH:
    for {
        select {
        case e := <-s.streamCh:
            flushEvent(e)
        case <-flushTimeout:
            break FLUSH
        default:
            break FLUSH
        }
    }
```

**Impact**: Lost events during shutdown, inconsistent snapshot state

---

#### 2. **Incomplete Disposal Pattern** (CRITICAL)
**Status**: ✅ Confirmed Real Issue

**Problem**:
```csharp
public void Dispose()
{
    _bufferedWriter?.Dispose();
    _fileHandle?.Dispose();
}
```
- Background tasks (`_teeTask`, `_streamTask`) not awaited
- Channels not completed
- Can leave tasks running after disposal
- No cancellation of timer in `StreamAsync`

**Go Reference**: Go goroutines exit when `shutdownCh` is closed; no explicit disposal needed

**Impact**: Resource leaks, zombie tasks, potential data corruption

---

#### 3. **Blocking I/O in Async Context** (HIGH)
**Status**: ✅ Confirmed Real Issue

**Locations**:
- Line 524: `_bufferedWriter?.Flush()`
- Line 525: `_fileHandle?.Flush()`
- Line 617: `_bufferedWriter.Flush()`
- Line 618: `_fileHandle!.Flush()`

**Problem**: Synchronous blocking calls in async methods

**Impact**: Thread pool starvation, reduced throughput

---

#### 4. **Unbounded Channel Risk** (HIGH)
**Status**: ✅ Confirmed Real Issue

**Problem**:
```csharp
var inCh = Channel.CreateUnbounded<Event>(...)  // Line 80
_streamCh = Channel.CreateUnbounded<Event>(...) // Line 166
```

**Go Reference** (snapshot.go line 110):
```go
inCh := make(chan Event, eventChSize)  // eventChSize = 2048
```

Go uses **bounded channels** with capacity 2048

**Impact**: Unbounded memory growth if processing is slower than event rate

---

#### 5. **Compaction Lock Duration** (MEDIUM)
**Status**: ✅ Confirmed Real Issue

**Problem**:
```csharp
lock (_fileLock)
{
    // ... (lines 685-704)
    File.Delete(_path);
    File.Move(newPath, _path);
    _fileHandle = new FileStream(_path, ...);
    // File I/O inside lock
}
```

**Impact**: All writes blocked during compaction (can take 100ms+)

---

#### 6. **Leave Event Synchronization** (LOW)
**Status**: ✅ Real but Minor

**Problem**:
```csharp
public async Task LeaveAsync()  // Line 229
{
    await HandleLeaveAsync();  // But HandleLeaveAsync doesn't coordinate with StreamAsync
}
```

- `LeaveAsync` is async but doesn't wait for write confirmation
- Race between direct call and channel processing

**Go Reference** (snapshot.go lines 199-204):
```go
func (s *Snapshotter) Leave() {
    select {
    case s.leaveCh <- struct{}{}:  // Non-blocking channel send
    case <-s.shutdownCh:
    }
}
```

**Impact**: Minor - leave marker might not be written before shutdown

---

### ❌ FALSE POSITIVES

#### 1. **Clock Update Race Condition**
**Status**: ❌ Not a Real Issue

**Claim**: "_lastClock read/write not protected by lock"

**Analysis**:
- `UpdateClock()` is **only** called from `StreamAsync` (single thread)
- Lines 352, 374, 398, 513 - all within `StreamAsync` task
- Go implementation has same pattern (no lock)

**Verdict**: No race condition exists

---

#### 2. **File Corruption from Offset Mismatch**
**Status**: ❌ Not a Real Issue

**Claim**: "Exception between write and offset update"

**Analysis**:
```csharp
lock (_fileLock)
{
    _bufferedWriter!.Write(line);    // Line 610
    // ... flush logic ...
    _offset += bytes;                 // Line 621
}
```
- Both write and offset update are **inside the same lock**
- If exception occurs, entire transaction rolls back
- Lock is not released until both complete

**Verdict**: Offset consistency is maintained

---

#### 3. **String Parsing Fragility**
**Status**: ⚠️ Minor Concern (not critical)

**Claim**: "No validation for malformed lines"

**Analysis**: Go implementation has identical parsing (snapshot.go lines 563-623)

**Verdict**: Low priority - matches reference implementation

---

## Improvement Checklist

### 🔴 Phase 1: Critical Fixes (Must Fix)

#### ☐ **1.1 Implement Proper Async Disposal**
**Priority**: P0 (Highest)  
**Estimated Effort**: 2 hours  
**Files**: `Snapshotter.cs`

**Changes Required**:
```csharp
// Add IAsyncDisposable interface
public class Snapshotter : IDisposable, IAsyncDisposable

// Implement DisposeAsync
public async ValueTask DisposeAsync()
{
    // 1. Complete input channel
    try
    {
        _inCh?.Complete();
    }
    catch { }
    
    // 2. Wait for tasks to finish
    var tasks = new List<Task>();
    if (_teeTask != null) tasks.Add(_teeTask);
    if (_streamTask != null) tasks.Add(_streamTask);
    
    if (tasks.Count > 0)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }
    
    // 3. Dispose resources
    Dispose();
}

// Keep sync Dispose for IDisposable
public void Dispose()
{
    _bufferedWriter?.Dispose();
    _fileHandle?.Dispose();
}
```

**Testing**:
- Add test: `DisposeAsync_ShouldWaitForTaskCompletion`
- Verify no resource leaks with 1000 rapid create/dispose cycles

---

#### ☐ **1.2 Fix Shutdown Race Condition**
**Priority**: P0  
**Estimated Effort**: 3 hours  
**Files**: `Snapshotter.cs`

**Changes Required**:

**Step 1**: Update `TeeStreamAsync` to respect shutdown:
```csharp
private async Task TeeStreamAsync()
{
    try
    {
        await foreach (var evt in _inCh.ReadAllAsync(_shutdownToken))
        {
            // Forward to stream channel (blocking)
            await _streamCh.Writer.WriteAsync(evt, _shutdownToken);
            
            // Forward to output channel if configured
            if (_outCh != null)
            {
                _outCh.TryWrite(evt);  // Non-blocking
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Shutdown signal received
    }
    finally
    {
        // Signal no more events coming
        _streamCh.Writer.Complete();
    }
}
```

**Step 2**: Update `PerformShutdownFlushAsync`:
```csharp
private async Task PerformShutdownFlushAsync()
{
    UpdateClock();

    // Process leave events first
    while (_leaveCh.Reader.TryRead(out var _))
    {
        await HandleLeaveAsync();
    }

    // Now drain _streamCh (TeeStream has completed it)
    var cts = new CancellationTokenSource(ShutdownFlushTimeoutMs);
    try
    {
        await foreach (var evt in _streamCh.Reader.ReadAllAsync(cts.Token))
        {
            FlushEvent(evt);
        }
    }
    catch (OperationCanceledException)
    {
        // Timeout reached - acceptable data loss
    }

    // Final flush
    await _bufferedWriter!.FlushAsync();
    await _fileHandle!.FlushAsync();
}
```

**Testing**:
- Test: `Shutdown_WithPendingEvents_ShouldNotLoseData`
- Flood with 10k events, shutdown immediately, verify all written

---

#### ☐ **1.3 Replace Unbounded Channels with Bounded**
**Priority**: P0  
**Estimated Effort**: 1 hour  
**Files**: `Snapshotter.cs`

**Changes Required**:
```csharp
// Line 80 - Make inCh bounded
var inCh = Channel.CreateBounded<Event>(new BoundedChannelOptions(EventChSize)
{
    SingleReader = true,
    SingleWriter = false,
    FullMode = BoundedChannelFullMode.Wait  // Block when full (backpressure)
});

// Line 166 - Make streamCh bounded
_streamCh = Channel.CreateBounded<Event>(new BoundedChannelOptions(EventChSize)
{
    SingleReader = true,
    SingleWriter = false,
    FullMode = BoundedChannelFullMode.Wait
});

// Line 46 - _leaveCh can stay unbounded (rarely used)
```

**Testing**:
- Test: `BoundedChannel_ShouldApplyBackpressure`
- Verify memory stays constant under sustained load

---

#### ☐ **1.4 Convert Blocking I/O to Async**
**Priority**: P0  
**Estimated Effort**: 2 hours  
**Files**: `Snapshotter.cs`

**Changes Required**:

**ForceFlush**:
```csharp
private async Task ForceFlushAsync()
{
    try
    {
        lock (_fileLock)
        {
            // Flush must be sync inside lock to maintain atomicity
            _bufferedWriter?.Flush();
        }
        
        // File flush can be async outside lock
        await (_fileHandle?.FlushAsync() ?? Task.CompletedTask);
        _lastFlush = DateTime.UtcNow;
    }
    catch (Exception ex)
    {
        DebugLog($"ForceFlush: error {ex.GetType().Name} {ex.Message}");
    }
}
```

**ProcessMemberEvent**:
```csharp
private async Task ProcessMemberEventAsync(MemberEvent e)
{
    // ... existing logic ...
    UpdateClock();
    await ForceFlushAsync();  // Make async
}
```

**Update StreamAsync loop**:
```csharp
case completed when completed == tEvent:
    var evt = await tEvent;
    await FlushEventAsync(evt);  // Make async
```

**Testing**:
- Benchmark: Compare throughput before/after
- Verify no thread pool exhaustion under load

---

### 🟡 Phase 2: High Priority Improvements

#### ☐ **2.1 Minimize Compaction Lock Duration**
**Priority**: P1  
**Estimated Effort**: 3 hours  
**Files**: `Snapshotter.cs`

**Changes Required**:
```csharp
private void Compact()
{
    // Step 1: Create new file OUTSIDE lock
    var newPath = _path + TmpExt;
    using (var newFile = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.None))
    using (var writer = new StreamWriter(newFile, Encoding.UTF8))
    {
        long offset = 0;

        // Snapshot alive nodes (with lock)
        Dictionary<string, string> snapshot;
        lock (_lock)
        {
            snapshot = new Dictionary<string, string>(_aliveNodes);
        }

        // Write to new file (no lock needed)
        foreach (var (name, addr) in snapshot)
        {
            var line = $"alive: {name} {addr}\n";
            writer.Write(line);
            offset += Encoding.UTF8.GetByteCount(line);
        }

        // Write clocks...
        writer.Flush();
        newFile.Flush(true);

        // Step 2: Swap files (SHORT lock)
        lock (_fileLock)
        {
            _bufferedWriter?.Flush();
            _bufferedWriter?.Dispose();
            _fileHandle?.Close();

            File.Delete(_path);
            File.Move(newPath, _path);

            _fileHandle = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            _bufferedWriter = new StreamWriter(_fileHandle, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
            _offset = offset;
            _lastFlush = DateTime.UtcNow;
        }
    }
}
```

**Testing**:
- Measure lock hold time before/after (should be <10ms)
- Verify no data corruption during concurrent writes + compaction

---

#### ☐ **2.2 Add Corruption Detection**
**Priority**: P1  
**Estimated Effort**: 4 hours  
**Files**: `Snapshotter.cs`

**Changes Required**:
```csharp
// Add CRC32 helper
private uint CalculateCrc32(string line)
{
    var bytes = Encoding.UTF8.GetBytes(line.TrimEnd('\n'));
    return Crc32.Compute(bytes);
}

// Update TryAppend to include checksum
private void TryAppend(string line)
{
    var crc = CalculateCrc32(line);
    var lineWithChecksum = $"{line.TrimEnd('\n')} #{crc:X8}\n";
    AppendLine(lineWithChecksum);
}

// Update ReplayAsync to validate
private async Task ReplayAsync()
{
    int corruptLines = 0;
    // ... existing code ...
    
    while ((line = await reader.ReadLineAsync()) != null)
    {
        // Extract and verify checksum
        var checksumIdx = line.LastIndexOf(" #");
        if (checksumIdx > 0)
        {
            var dataLine = line.Substring(0, checksumIdx);
            var checksumStr = line.Substring(checksumIdx + 2);
            
            if (uint.TryParse(checksumStr, System.Globalization.NumberStyles.HexNumber, null, out var storedCrc))
            {
                var calculatedCrc = CalculateCrc32(dataLine + "\n");
                if (calculatedCrc != storedCrc)
                {
                    _logger?.LogWarning("Corrupt line detected: {Line}", line);
                    corruptLines++;
                    continue;
                }
            }
            
            line = dataLine;
        }
        
        // ... process line ...
    }
    
    if (corruptLines > 0)
    {
        _logger?.LogWarning("Detected {Count} corrupt lines during replay", corruptLines);
    }
}
```

**Testing**:
- Test: `Replay_WithCorruptedFile_ShouldSkipBadLines`
- Manually corrupt snapshot file, verify recovery

---

#### ☐ **2.3 Fix Leave Synchronization**
**Priority**: P1  
**Estimated Effort**: 1 hour  
**Files**: `Snapshotter.cs`, `Serf.cs`

**Changes Required**:
```csharp
// Make Leave fully async-coordinated
public async Task LeaveAsync()
{
    var tcs = new TaskCompletionSource<bool>();
    
    // Send leave request with completion signal
    await _leaveCh.Writer.WriteAsync((true, tcs));
    
    // Wait for confirmation
    await tcs.Task;
}

private async Task HandleLeaveAsync()
{
    _leaving = true;

    if (!_rejoinAfterLeave)
    {
        lock (_lock)
        {
            _aliveNodes.Clear();
        }
    }

    TryAppend("leave\n");
    
    await _bufferedWriter!.FlushAsync();
    await _fileHandle!.FlushAsync();
}
```

**Testing**:
- Test: `Leave_ShouldFlushBeforeReturning`
- Verify leave marker always written

---

### 🟢 Phase 3: Medium Priority Optimizations

#### ☐ **3.1 Add Metrics and Observability**
**Priority**: P2  
**Estimated Effort**: 3 hours  
**Files**: `Snapshotter.cs`

**Metrics to Add**:
```csharp
// Add properties
public long TotalEventsProcessed { get; private set; }
public long TotalCompactions { get; private set; }
public long TotalFlushes { get; private set; }
public TimeSpan AverageFlushTime { get; private set; }
public int CurrentQueueDepth => _streamCh.Reader.Count;

// Instrument key operations
private async Task FlushEventAsync(Event e)
{
    var sw = Stopwatch.StartNew();
    // ... existing logic ...
    TotalEventsProcessed++;
    
    // Update rolling average
    AverageFlushTime = TimeSpan.FromMilliseconds(
        (AverageFlushTime.TotalMilliseconds * 0.95) + (sw.Elapsed.TotalMilliseconds * 0.05)
    );
}
```

**Testing**:
- Verify metrics accuracy under load

---

#### ☐ **3.2 Reduce Allocations in Hot Path**
**Priority**: P2  
**Estimated Effort**: 2 hours  
**Files**: `Snapshotter.cs`

**Changes Required**:
```csharp
// Use Span<char> for parsing
private bool TryParseAliveLine(ReadOnlySpan<char> line, out string name, out string addr)
{
    const string prefix = "alive: ";
    if (!line.StartsWith(prefix))
    {
        name = string.Empty;
        addr = string.Empty;
        return false;
    }
    
    var data = line.Slice(prefix.Length);
    var lastSpace = data.LastIndexOf(' ');
    
    if (lastSpace == -1)
    {
        name = string.Empty;
        addr = string.Empty;
        return false;
    }
    
    name = data.Slice(0, lastSpace).ToString();
    addr = data.Slice(lastSpace + 1).ToString();
    return true;
}

// Use StringBuilder for compaction
private void Compact()
{
    var sb = new StringBuilder(4096);
    // Build file content in memory, then write once
}
```

**Testing**:
- Benchmark: Measure allocation rate before/after
- Target: <50% allocation reduction

---

#### ☐ **3.3 Implement Write-Ahead Logging**
**Priority**: P2  
**Estimated Effort**: 8 hours  
**Files**: `Snapshotter.cs`, new `WriteAheadLog.cs`

**Pattern**:
```
1. Write to WAL (sequential, fast)
2. Periodically checkpoint to snapshot
3. On recovery: Replay snapshot + WAL
4. Truncate WAL after successful checkpoint
```

**Benefits**:
- Better crash recovery
- Reduced fsync frequency
- Can batch writes

---

### 🔵 Phase 4: Low Priority Enhancements

#### ☐ **4.1 Structured Snapshot Format**
**Priority**: P3  
**Estimated Effort**: 6 hours

Replace text format with binary length-prefixed records:
```
[4 bytes: record length][1 byte: type][N bytes: data][4 bytes: CRC32]
```

**Benefits**:
- Faster parsing
- Better corruption detection
- More compact

---

#### ☐ **4.2 Memory-Mapped File Support**
**Priority**: P3  
**Estimated Effort**: 5 hours

Use `MemoryMappedFile` for large snapshots (>10MB)

**Benefits**:
- Zero-copy reads
- OS-managed caching

---

#### ☐ **4.3 Snapshot Versioning**
**Priority**: P3  
**Estimated Effort**: 2 hours

Add version header to snapshot file:
```
# NSerf Snapshot v2
# Created: 2025-01-19T18:00:00Z
```

**Benefits**:
- Forward/backward compatibility
- Easier debugging

---

## Testing Strategy

### Unit Tests to Add

```csharp
[Fact]
public async Task Snapshot_UnderLoad_ShouldNotLoseEvents()
{
    // Send 10k events, verify all written
}

[Fact]
public async Task DisposeAsync_ShouldWaitForPendingWrites()
{
    // Start write, dispose immediately, verify write completes
}

[Fact]
public async Task BoundedChannel_ShouldApplyBackpressure()
{
    // Fill channel, verify TryWrite blocks
}

[Fact]
public async Task Compaction_WithConcurrentWrites_ShouldNotCorrupt()
{
    // Write continuously, trigger compaction, verify consistency
}

[Fact]
public async Task Shutdown_WithPendingEvents_ShouldDrainGracefully()
{
    // Queue 1k events, shutdown with 250ms timeout, verify drain
}
```

### Integration Tests

```csharp
[Fact]
public async Task EndToEnd_CrashRecovery()
{
    // 1. Create cluster, write events
    // 2. Kill process (no graceful shutdown)
    // 3. Restart, verify recovery
}

[Fact]
public async Task EndToEnd_HighThroughput()
{
    // Sustain 1000 events/sec for 60 seconds
    // Verify memory stable, no leaks
}
```

---

## Implementation Priority

### Week 1: Critical Fixes
- **Day 1-2**: Fix shutdown race (1.2)
- **Day 3**: Implement async disposal (1.1)
- **Day 4**: Convert to bounded channels (1.3)
- **Day 5**: Convert blocking I/O (1.4)

### Week 2: High Priority
- **Day 1-2**: Minimize compaction lock (2.1)
- **Day 3-4**: Add corruption detection (2.2)
- **Day 5**: Fix leave sync (2.3)

### Week 3+: Medium/Low Priority
- Implement as time permits based on production needs

---

## Success Metrics

### Performance Targets
- ✅ **Throughput**: 5000+ events/sec sustained
- ✅ **Latency**: p99 < 10ms for event flush
- ✅ **Memory**: Stable under load (no growth)
- ✅ **Lock Hold Time**: <5ms for compaction

### Reliability Targets
- ✅ **Crash Recovery**: 100% event recovery with WAL
- ✅ **Corruption Detection**: Catch 99.9% of bit flips
- ✅ **Graceful Shutdown**: Zero data loss

---

## Risk Assessment

### Low Risk Changes
- Clock update (false positive)
- Metrics (2.1)
- Leave sync (2.3)

### Medium Risk Changes
- Bounded channels (1.3) - requires careful testing
- Compaction lock reduction (2.1) - risk of corruption

### High Risk Changes
- Shutdown coordination (1.2) - complex state machine
- WAL implementation (3.3) - new subsystem

---

## Compatibility Notes

All changes maintain **backward compatibility** with:
- ✅ Existing snapshot file format
- ✅ Go Serf interoperability
- ✅ Public API surface

File format changes (CRC, binary format) would require migration path.

---

## References

- **Go Implementation**: `serf/serf/snapshot.go`
- **C# Channels**: https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/
- **Async Disposal**: https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-disposeasync

---

## Appendix: Benchmarking Commands

```bash
# Run snapshot benchmarks
dotnet test --filter "Category=Snapshot" --configuration Release

# Profile memory
dotnet-counters monitor --process-id <PID> System.Runtime

# Measure throughput
dotnet run --project SnapshotBenchmark -- --events 100000
```

---

**Document Version**: 1.0  
**Last Updated**: 2025-01-19  
**Reviewed By**: Code Analysis + Go Reference Comparison
