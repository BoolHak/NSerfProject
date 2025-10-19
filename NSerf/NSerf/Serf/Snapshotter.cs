using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSerf.Serf.Events;

namespace NSerf.Serf;

/// <summary>
/// Serf supports using a "snapshot" file that contains various
/// transactional data that is used to help Serf recover quickly
/// and gracefully from a failure. We append member events, as well
/// as the latest clock values to the file during normal operation,
/// and periodically checkpoint and roll over the file. During a restore,
/// we can replay the various member events to recall a list of known
/// nodes to re-join, as well as restore our clock values to avoid replaying
/// old events.
/// </summary>
public class Snapshotter : IDisposable, IAsyncDisposable
{
    private const int FlushIntervalMs = 500;
    private const int ClockUpdateIntervalMs = 500;
    private const string TmpExt = ".compact";
    private const int SnapshotErrorRecoveryIntervalMs = 30000;
    private const int EventChSize = 2048;
    private const int ShutdownFlushTimeoutMs = 250;
    private const int SnapshotBytesPerNode = 128;
    private const int SnapshotCompactionThreshold = 2;

    private readonly Dictionary<string, string> _aliveNodes = new();
    private readonly LamportClock _clock;
    private FileStream? _fileHandle;
    private StreamWriter? _bufferedWriter;
    private readonly ChannelReader<Event> _inCh;
    private readonly Channel<Event> _streamCh;
    private DateTime _lastFlush = DateTime.UtcNow;
    private LamportTime _lastClock;
    private LamportTime _lastEventClock;
    private LamportTime _lastQueryClock;
    private readonly Channel<bool> _leaveCh = Channel.CreateUnbounded<bool>();
    private bool _leaving;
    private readonly ILogger? _logger;
    private readonly long _minCompactSize;
    private readonly string _path;
    private long _offset;
    private readonly ChannelWriter<Event>? _outCh;
    private readonly string _debugLogPath;
    private readonly bool _rejoinAfterLeave;
    private readonly CancellationToken _shutdownToken;
    private readonly TaskCompletionSource _waitTcs = new();
    private DateTime _lastAttemptedCompaction = DateTime.MinValue;
    private readonly object _lock = new();
    private Task? _teeTask;
    private Task? _streamTask;
    private readonly object _fileLock = new();

    /// <summary>
    /// Creates a new Snapshotter that records events up to a
    /// max byte size before rotating the file. It can also be used to
    /// recover old state. Snapshotter works by reading an event channel it returns,
    /// passing through to an output channel, and persisting relevant events to disk.
    /// Setting rejoinAfterLeave makes leave not clear the state, and can be used
    /// if you intend to rejoin the same cluster after a leave.
    /// </summary>
    public static async Task<(ChannelWriter<Event> InCh, Snapshotter Snap)> NewSnapshotterAsync(
        string path,
        int minCompactSize,
        bool rejoinAfterLeave,
        ILogger? logger,
        LamportClock clock,
        ChannelWriter<Event>? outCh,
        CancellationToken shutdownToken)
    {
        var inCh = Channel.CreateBounded<Event>(new BoundedChannelOptions(EventChSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait  // Apply backpressure when full
        });

        // Try to open the file with retry logic to handle file locks from previous instances
        FileStream fh;
        int retries = 5;
        while (true)
        {
            try
            {
                fh = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                break;
            }
            catch (IOException ex) when (retries > 0)
            {
                retries--;
                logger?.LogWarning("Failed to open snapshot (retries left: {Retries}): {Error}", retries, ex.Message);
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to open snapshot: {ex.Message}", ex);
            }
        }

        // Determine the offset
        long offset = fh.Length;

        // Create the snapshotter
        var snap = new Snapshotter(
            aliveNodes: new Dictionary<string, string>(),
            clock: clock,
            fileHandle: fh,
            inCh: inCh.Reader,
            lastClock: new LamportTime(0),
            lastEventClock: new LamportTime(0),
            lastQueryClock: new LamportTime(0),
            logger: logger,
            minCompactSize: minCompactSize,
            path: path,
            offset: offset,
            outCh: outCh,
            rejoinAfterLeave: rejoinAfterLeave,
            shutdownToken: shutdownToken);

        // Recover the last known state
        try
        {
            await snap.ReplayAsync();
        }
        catch (Exception)
        {
            fh.Close();
            throw;
        }

        // Start handling new commands
        snap.StartProcessing();

        return (inCh.Writer, snap);
    }

    private Snapshotter(
        Dictionary<string, string> aliveNodes,
        LamportClock clock,
        FileStream fileHandle,
        ChannelReader<Event> inCh,
        LamportTime lastClock,
        LamportTime lastEventClock,
        LamportTime lastQueryClock,
        ILogger? logger,
        long minCompactSize,
        string path,
        long offset,
        ChannelWriter<Event>? outCh,
        bool rejoinAfterLeave,
        CancellationToken shutdownToken)
    {
        _aliveNodes = aliveNodes;
        _clock = clock;
        _fileHandle = fileHandle;
        _bufferedWriter = new StreamWriter(fileHandle, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
        _inCh = inCh;
        _streamCh = Channel.CreateBounded<Event>(new BoundedChannelOptions(EventChSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait  // Apply backpressure when full
        });
        _lastClock = lastClock;
        _lastEventClock = lastEventClock;
        _lastQueryClock = lastQueryClock;
        _logger = logger;
        _minCompactSize = minCompactSize;
        _path = path;
        _offset = offset;
        _outCh = outCh;
        _rejoinAfterLeave = rejoinAfterLeave;
        _shutdownToken = shutdownToken;
        _debugLogPath = path + ".log";
    }

    /// <summary>
    /// Returns the last known clock time
    /// </summary>
    public LamportTime LastClock => _lastClock;

    /// <summary>
    /// Returns the last known event clock time
    /// </summary>
    public LamportTime LastEventClock => _lastEventClock;

    /// <summary>
    /// Returns the last known query clock time
    /// </summary>
    public LamportTime LastQueryClock => _lastQueryClock;

    /// <summary>
    /// Returns the last known alive nodes (in randomized order to prevent hot shards)
    /// </summary>
    public List<PreviousNode> AliveNodes()
    {
        lock (_lock)
        {
            var previous = _aliveNodes.Select(kvp => new PreviousNode(kvp.Key, kvp.Value)).ToList();
            
            // Randomize the order (Fisher-Yates shuffle)
            var rng = new Random();
            for (int i = previous.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (previous[i], previous[j]) = (previous[j], previous[i]);
            }
            
            return previous;
        }
    }

    /// <summary>
    /// Wait is used to wait until the snapshotter finishes shut down
    /// </summary>
    public Task WaitAsync() => _waitTcs.Task;

    /// <summary>
    /// Leave is used to remove known nodes to prevent a restart from
    /// causing a join. Otherwise nodes will re-join after leaving!
    /// </summary>
    public async Task LeaveAsync()
    {
        // Process leave immediately and synchronously to ensure it's written before shutdown
        await HandleLeaveAsync();
    }

    private void StartProcessing()
    {
        DebugLog("StartProcessing: starting TeeStream and Stream tasks");
        _logger?.LogInformation("[Snapshotter/StartProcessing] Starting TeeStream and Stream tasks...");
        _teeTask = Task.Run(TeeStreamAsync, _shutdownToken);
        _streamTask = Task.Run(StreamAsync, _shutdownToken);
        DebugLog("StartProcessing: tasks started");
        _logger?.LogInformation("[Snapshotter/StartProcessing] Tasks started successfully");
    }

    /// <summary>
    /// teeStream is a long running routine that is used to copy events
    /// to the output channel and the internal event handler.
    /// </summary>
    private async Task TeeStreamAsync()
    {
        DebugLog("TeeStream: started");
        _logger?.LogInformation("[Snapshotter/TeeStream] Task started, waiting for events...");
        try
        {
            while (await _inCh.WaitToReadAsync(_shutdownToken))
            {
                while (_inCh.TryRead(out var evt))
                {
                    DebugLog($"TeeStream: received {evt.GetType().Name}");
                    _logger?.LogInformation("[Snapshotter/TeeStream] Received event: {Type}", evt.GetType().Name);
                    
                    // Forward to stream channel
                    await _streamCh.Writer.WriteAsync(evt, _shutdownToken);
                    
                    // Forward to output channel if configured
                    if (_outCh != null)
                    {
                        await _outCh.WriteAsync(evt, _shutdownToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            DebugLog("TeeStream: cancelled");
            _logger?.LogInformation("[Snapshotter/TeeStream] Task cancelled (shutdown)");
        }
    }

    /// <summary>
    /// stream is a long running routine that is used to handle events
    /// </summary>
    private async Task StreamAsync()
    {
        DebugLog("Stream: started");
        _logger?.LogInformation("[Snapshotter/Stream] Task started, processing events...");
        using var clockTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(ClockUpdateIntervalMs));

        try
        {
            while (!_shutdownToken.IsCancellationRequested)
            {
                // Create tasks once per iteration so we can compare references reliably
                var tLeave = _leaveCh.Reader.ReadAsync(_shutdownToken).AsTask();
                var tEvent = _streamCh.Reader.ReadAsync(_shutdownToken).AsTask();
                Task<bool> tClock;
                
                try
                {
                    tClock = clockTimer.WaitForNextTickAsync(_shutdownToken).AsTask();
                }
                catch (ObjectDisposedException)
                {
                    // Timer was disposed, treat as shutdown
                    DebugLog("Stream: clockTimer disposed, exiting");
                    break;
                }

                var completed = await Task.WhenAny(tLeave, tEvent, tClock);

                if (completed == tLeave)
                {
                    DebugLog("Stream: processing leave event");
                    _logger?.LogInformation("[Snapshotter/Stream] Processing leave event");
                    await HandleLeaveAsync();
                }
                else if (completed == tEvent)
                {
                    if (tEvent.IsCanceled)
                    {
                        DebugLog("Stream: event task canceled");
                        continue;
                    }
                    if (tEvent.IsFaulted)
                    {
                        var ex = tEvent.Exception?.GetBaseException();
                        DebugLog($"Stream: event task faulted {ex?.GetType().Name} {ex?.Message}");
                        // If channel closed, break the loop gracefully; otherwise continue
                        if (ex is InvalidOperationException)
                        {
                            DebugLog("Stream: event channel closed, exiting loop");
                            break;
                        }
                        continue;
                    }
                    var evt = await tEvent;
                    DebugLog($"Stream: processing event {evt.GetType().Name}");
                    _logger?.LogInformation("[Snapshotter/Stream] Processing event: {Type}", evt.GetType().Name);
                    try
                    {
                        FlushEvent(evt);
                        DebugLog($"Stream: successfully flushed event {evt.GetType().Name}");
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"Stream: FlushEvent error {ex.GetType().Name} {ex.Message} {ex.StackTrace}");
                        throw;
                    }
                }
                else if (completed == tClock)
                {
                    UpdateClock();
                }
            }
        }
        catch (OperationCanceledException)
        {
            DebugLog("Stream: cancelled (OperationCanceledException)");
            await PerformShutdownFlushAsync();
        }
        catch (InvalidOperationException ex) when (_shutdownToken.IsCancellationRequested)
        {
            // PeriodicTimer can throw InvalidOperationException during shutdown
            DebugLog($"Stream: cancelled (InvalidOperationException during shutdown): {ex.Message}");
            await PerformShutdownFlushAsync();
        }
        catch (Exception ex)
        {
            DebugLog($"Stream: exception {ex.GetType().Name} {ex.Message}");
            _logger?.LogError(ex, "[Snapshotter/Stream] Unhandled exception");
            // Best-effort flush to persist any buffered lines before exiting
            try
            {
                UpdateClock();
                await _bufferedWriter!.FlushAsync();
                await _fileHandle!.FlushAsync();
                DebugLog("Stream: flushed after exception");
            }
            catch (Exception flushEx)
            {
                DebugLog($"Stream: flush-after-exception error {flushEx.GetType().Name} {flushEx.Message}");
            }
        }
        finally
        {
            DebugLog("Stream: exiting");
            _fileHandle?.Close();
            _waitTcs.SetResult();
        }
    }

    private async Task PerformShutdownFlushAsync()
    {
        // Shutdown - flush remaining events with timeout
        var cts = new CancellationTokenSource(ShutdownFlushTimeoutMs);
        
        // Snapshot the clock
        UpdateClock();

        // Process any pending leave events FIRST
        while (_leaveCh.Reader.TryRead(out var _))
        {
            DebugLog("Stream: processing pending leave during shutdown");
            await HandleLeaveAsync();
        }

        // Drain remaining events
        while (_streamCh.Reader.TryRead(out var evt))
        {
            if (cts.Token.IsCancellationRequested) break;
            DebugLog($"Stream: draining event {evt.GetType().Name}");
            FlushEvent(evt);
        }

        // Final flush
        try
        {
            DebugLog("Stream: final flush");
            await _bufferedWriter!.FlushAsync();
            await _fileHandle!.FlushAsync();
        }
        catch (Exception ex)
        {
            DebugLog($"Stream: flush error {ex.Message}");
            _logger?.LogError(ex, "Failed to flush snapshot during shutdown");
        }
    }

    private async Task HandleLeaveAsync()
    {
        _leaving = true;

        // If we plan to re-join, keep our state
        if (!_rejoinAfterLeave)
        {
            lock (_lock)
            {
                _aliveNodes.Clear();
            }
        }

        TryAppend("leave\n");
        
        try
        {
            await _bufferedWriter!.FlushAsync();
            await _fileHandle!.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to flush leave to snapshot");
        }
    }

    private void FlushEvent(Event e)
    {
        // Stop recording events after a leave is issued
        if (_leaving) return;

        switch (e)
        {
            case MemberEvent memberEvent:
                ProcessMemberEvent(memberEvent);
                break;
            case UserEvent userEvent:
                ProcessUserEvent(userEvent);
                break;
            case Query query:
                ProcessQuery(query);
                break;
            default:
                _logger?.LogError("Unknown event to snapshot: {EventType}", e.GetType().Name);
                break;
        }
    }

    private void ProcessMemberEvent(MemberEvent e)
    {
        DebugLog($"ProcessMemberEvent: {e.EventType()} members={e.Members.Count}");
        _logger?.LogInformation("[Snapshotter] Processing MemberEvent: {Type} with {Count} members", 
            e.EventType(), e.Members.Count);
        
        lock (_lock)
        {
            switch (e.EventType())
            {
                case EventType.MemberJoin:
                    foreach (var mem in e.Members)
                    {
                        var addr = $"{mem.Addr}:{mem.Port}";
                        _aliveNodes[mem.Name] = addr;
                        DebugLog($"ProcessMemberEvent: alive {mem.Name} {addr}");
                        _logger?.LogInformation("[Snapshotter] Recording alive node: {Name} at {Addr}", mem.Name, addr);
                        TryAppend($"alive: {mem.Name} {addr}\n");
                    }
                    break;

                case EventType.MemberLeave:
                case EventType.MemberFailed:
                    foreach (var mem in e.Members)
                    {
                        _aliveNodes.Remove(mem.Name);
                        DebugLog($"ProcessMemberEvent: not-alive {mem.Name}");
                        _logger?.LogInformation("[Snapshotter] Recording not-alive node: {Name}", mem.Name);
                        TryAppend($"not-alive: {mem.Name}\n");
                    }
                    break;
            }
            
            DebugLog($"ProcessMemberEvent: total alive {_aliveNodes.Count}");
            _logger?.LogInformation("[Snapshotter] Total alive nodes in memory: {Count}", _aliveNodes.Count);
        }
        UpdateClock();
        // Force an immediate flush to make snapshot visible promptly
        ForceFlush();
    }

    private void ForceFlush()
    {
        try
        {
            lock (_fileLock)
            {
                _bufferedWriter?.Flush();
                _fileHandle?.Flush();
                _lastFlush = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            DebugLog($"ForceFlush: error {ex.GetType().Name} {ex.Message}");
        }
    }

    private void DebugLog(string message)
    {
        try
        {
            File.AppendAllText(_debugLogPath, $"[{DateTime.UtcNow:O}] {message}\n");
        }
        catch
        {
            // Ignore logging errors
        }
    }

    private void UpdateClock()
    {
        var lastSeen = _clock.Time() - 1;
        if (lastSeen > _lastClock)
        {
            _lastClock = lastSeen;
            TryAppend($"clock: {_lastClock}\n");
        }
    }

    private void ProcessUserEvent(UserEvent e)
    {
        // Ignore old clocks
        if (e.LTime <= _lastEventClock) return;
        
        _lastEventClock = e.LTime;
        TryAppend($"event-clock: {e.LTime}\n");
    }

    private void ProcessQuery(Query q)
    {
        // Ignore old clocks
        if (q.LTime <= _lastQueryClock) return;
        
        _lastQueryClock = q.LTime;
        TryAppend($"query-clock: {q.LTime}\n");
    }

    private void TryAppend(string line)
    {
        try
        {
            AppendLine(line);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update snapshot");
            
            var now = DateTime.UtcNow;
            if ((now - _lastAttemptedCompaction).TotalMilliseconds > SnapshotErrorRecoveryIntervalMs)
            {
                _lastAttemptedCompaction = now;
                _logger?.LogInformation("Attempting compaction to recover from error...");
                
                try
                {
                    Compact();
                    _logger?.LogInformation("Finished compaction, successfully recovered from error state");
                }
                catch (Exception compactEx)
                {
                    _logger?.LogError(compactEx, "Compaction failed, will reattempt after {Interval}ms", SnapshotErrorRecoveryIntervalMs);
                }
            }
        }
    }

    private void AppendLine(string line)
    {
        var bytes = Encoding.UTF8.GetByteCount(line);

        lock (_fileLock)
        {
            _bufferedWriter!.Write(line);

            // Check if we should flush
            var now = DateTime.UtcNow;
            if ((now - _lastFlush).TotalMilliseconds > FlushIntervalMs)
            {
                _lastFlush = now;
                _bufferedWriter.Flush();
                _fileHandle!.Flush();
            }

            _offset += bytes;
        }

        // Check compaction outside the file lock to avoid long-held locks during I/O
        if (_offset > SnapshotMaxSize())
        {
            Compact();
        }
    }

    private long SnapshotMaxSize()
    {
        lock (_lock)
        {
            long nodes = _aliveNodes.Count;
            long estSize = nodes * SnapshotBytesPerNode;
            long threshold = estSize * SnapshotCompactionThreshold;

            // Apply minimum threshold
            if (threshold < _minCompactSize)
            {
                threshold = _minCompactSize;
            }
            return threshold;
        }
    }

    private void Compact()
    {
        var newPath = _path + TmpExt;

        using (var newFile = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(newFile, Encoding.UTF8))
        {
            long offset = 0;

            // Write out alive nodes (protect access to _aliveNodes with _lock)
            lock (_lock)
            {
                foreach (var (name, addr) in _aliveNodes)
                {
                    var line = $"alive: {name} {addr}\n";
                    writer.Write(line);
                    offset += Encoding.UTF8.GetByteCount(line);
                }
            }

            // Write out clocks
            var clockLine = $"clock: {_lastClock}\n";
            writer.Write(clockLine);
            offset += Encoding.UTF8.GetByteCount(clockLine);

            var eventClockLine = $"event-clock: {_lastEventClock}\n";
            writer.Write(eventClockLine);
            offset += Encoding.UTF8.GetByteCount(eventClockLine);

            var queryClockLine = $"query-clock: {_lastQueryClock}\n";
            writer.Write(queryClockLine);
            offset += Encoding.UTF8.GetByteCount(queryClockLine);

            writer.Flush();
            newFile.Flush(true);

            // Swap files with file lock held to avoid races with AppendLine/StreamAsync
            lock (_fileLock)
            {
                try
                {
                    _bufferedWriter?.Flush();
                    _bufferedWriter?.Dispose();
                    _fileHandle?.Close();
                }
                catch { /* ignore */ }

                // Replace old file with new file
                try { File.Delete(_path); } catch { }
                File.Move(newPath, _path);

                // Reopen
                _fileHandle = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
                _bufferedWriter = new StreamWriter(_fileHandle, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
                _offset = offset;
                _lastFlush = DateTime.UtcNow;
            }
        }
    }
    /// <summary>
    /// replay is used to reset our internal state by replaying
    /// the snapshot file. It is used at initialization time to read old state.
    /// </summary>
    private async Task ReplayAsync()
    {
        // Seek to the beginning
        _fileHandle!.Seek(0, SeekOrigin.Begin);

        // Read each line
        using var reader = new StreamReader(_fileHandle, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("alive: "))
            {
                var info = line.Substring(7); // Remove "alive: "
                var lastSpaceIdx = info.LastIndexOf(' ');
                if (lastSpaceIdx == -1)
                {
                    _logger?.LogWarning("Failed to parse address: {Line}", line);
                    continue;
                }
                var addr = info.Substring(lastSpaceIdx + 1);
                var name = info.Substring(0, lastSpaceIdx);
                _aliveNodes[name] = addr;
            }
            else if (line.StartsWith("not-alive: "))
            {
                var name = line.Substring(11);
                _aliveNodes.Remove(name);
            }
            else if (line.StartsWith("clock: "))
            {
                var timeStr = line.Substring(7);
                if (ulong.TryParse(timeStr, out var time))
                {
                    _lastClock = new LamportTime(time);
                }
                else
                {
                    _logger?.LogWarning("Failed to parse clock time: {TimeStr}", timeStr);
                }
            }
            else if (line.StartsWith("event-clock: "))
            {
                var timeStr = line.Substring(13);
                if (ulong.TryParse(timeStr, out var time))
                {
                    _lastEventClock = new LamportTime(time);
                }
                else
                {
                    _logger?.LogWarning("Failed to parse event clock time: {TimeStr}", timeStr);
                }
            }
            else if (line.StartsWith("query-clock: "))
            {
                var timeStr = line.Substring(13);
                if (ulong.TryParse(timeStr, out var time))
                {
                    _lastQueryClock = new LamportTime(time);
                }
                else
                {
                    _logger?.LogWarning("Failed to parse query clock time: {TimeStr}", timeStr);
                }
            }
            else if (line.StartsWith("coordinate: "))
            {
                // Ignore old coordinates
                continue;
            }
            else if (line == "leave")
            {
                // Ignore leave if we plan on re-joining
                if (_rejoinAfterLeave)
                {
                    _logger?.LogInformation("Ignoring previous leave in snapshot");
                    continue;
                }
                _aliveNodes.Clear();
                _lastClock = new LamportTime(0);
                _lastEventClock = new LamportTime(0);
                _lastQueryClock = new LamportTime(0);
            }
            else if (line.StartsWith("#"))
            {
                // Skip comment lines
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                _logger?.LogWarning("Unrecognized snapshot line: {Line}", line);
            }
        }

        // Seek to the end for appending
        _fileHandle.Seek(0, SeekOrigin.End);
    }

    /// <summary>
    /// Disposes the snapshotter asynchronously, waiting for background tasks to complete.
    /// This is the preferred disposal method as it ensures all pending writes are flushed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Signal no more events will be written to streamCh
        try
        {
            _streamCh.Writer.Complete();
        }
        catch
        {
            // Channel may already be completed
        }

        // Wait for background tasks to finish
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
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error waiting for snapshotter tasks to complete");
            }
        }
        
        // Dispose resources synchronously
        Dispose();
    }

    /// <summary>
    /// Synchronous disposal. Use DisposeAsync() when possible.
    /// </summary>
    public void Dispose()
    {
        try
        {
            _bufferedWriter?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by StreamAsync
        }
        
        try
        {
            _fileHandle?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by StreamAsync
        }
    }
}

/// <summary>
/// PreviousNode is used to represent the previously known alive nodes
/// </summary>
public record PreviousNode(string Name, string Addr)
{
    public override string ToString() => $"{Name}: {Addr}";
}
