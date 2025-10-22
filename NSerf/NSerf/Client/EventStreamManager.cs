using NSerf.Serf.Events;
using System.Threading.Channels;

namespace NSerf.Client;

/// <summary>
/// Manages event streaming from Serf's EventCh to multiple IPC clients.
/// Reads events from a channel and fans them out to registered EventStream instances.
/// Phase 16 - Task 1.1 (TDD Implementation).
/// </summary>
internal class EventStreamManager
{
    private readonly ChannelReader<Event> _eventSource;
    private readonly Dictionary<ulong, EventStreamRegistration> _activeStreams = new();
    private readonly object _lock = new();
    private Task? _backgroundTask;
    private CancellationToken _cancellationToken;

    public EventStreamManager(ChannelReader<Event> eventSource)
    {
        _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
    }

    /// <summary>
    /// Starts the background task that reads events and fans them out.
    /// </summary>
    public void Start(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _backgroundTask = Task.Run(() => ReadEventsAsync(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Registers a new event stream for a client.
    /// </summary>
    public void RegisterStream(ulong seq, object client, string eventType, List<Event> receivedEvents, CancellationToken ct)
    {
        var eventStream = new EventStream(client, seq, eventType, ct);
        
        lock (_lock)
        {
            _activeStreams[seq] = new EventStreamRegistration
            {
                Seq = seq,
                EventType = eventType,
                ReceivedEvents = receivedEvents,
                CancellationToken = ct,
                Stream = eventStream
            };
        }
    }

    /// <summary>
    /// Unregisters an event stream.
    /// </summary>
    public void UnregisterStream(ulong seq)
    {
        lock (_lock)
        {
            _activeStreams.Remove(seq);
        }
    }

    /// <summary>
    /// Background task that reads events from EventCh and fans them out.
    /// </summary>
    private async Task ReadEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in _eventSource.ReadAllAsync(cancellationToken))
            {
                // Fan-out to all registered streams
                List<EventStreamRegistration> streams;
                lock (_lock)
                {
                    streams = _activeStreams.Values.ToList();
                }

                foreach (var streamReg in streams)
                {
                    if (MatchesFilter(evt, streamReg.EventType))
                    {
                        // Add to received events list (for testing)
                        streamReg.ReceivedEvents.Add(evt);
                        
                        // Phase 16: Actually send to client
                        if (streamReg.Stream != null)
                        {
                            try
                            {
                                await streamReg.Stream.SendEventAsync(evt);
                            }
                            catch
                            {
                                // Client disconnected, will be cleaned up later
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private bool MatchesFilter(Event evt, string filterType)
    {
        if (filterType == "*") return true;

        var eventType = GetEventType(evt);
        return string.Equals(eventType, filterType, StringComparison.OrdinalIgnoreCase);
    }

    private string GetEventType(Event evt)
    {
        return evt switch
        {
            MemberEvent me => me.Type switch
            {
                EventType.MemberJoin => "member-join",
                EventType.MemberLeave => "member-leave",
                EventType.MemberFailed => "member-failed",
                EventType.MemberUpdate => "member-update",
                EventType.MemberReap => "member-reap",
                _ => "unknown"
            },
            UserEvent => "user",
            Query => "query",
            _ => "unknown"
        };
    }

    private class EventStreamRegistration
    {
        public ulong Seq { get; set; }
        public string EventType { get; set; } = string.Empty;
        public List<Event> ReceivedEvents { get; set; } = new();
        public CancellationToken CancellationToken { get; set; }
        public EventStream? Stream { get; set; }
    }
}
