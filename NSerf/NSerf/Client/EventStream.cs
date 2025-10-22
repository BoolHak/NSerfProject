using NSerf.Serf.Events;

namespace NSerf.Client;

/// <summary>
/// Handles per-client event filtering and delivery.
/// Filters events based on type and sends to IPC client.
/// Phase 16 - Task 2.1 (TDD Implementation).
/// </summary>
internal class EventStream
{
    private readonly object _client;
    private readonly ulong _seq;
    private readonly string _filterType;
    private readonly CancellationToken _cancellationToken;

    public EventStream(object client, ulong seq, string filterType, CancellationToken cancellationToken)
    {
        _client = client;
        _seq = seq;
        _filterType = filterType;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Checks if an event matches this stream's filter.
    /// </summary>
    public bool MatchesFilter(Event evt)
    {
        if (_filterType == "*") return true;

        var eventType = GetEventType(evt);
        return string.Equals(eventType, _filterType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sends an event to the client.
    /// </summary>
    public async Task SendEventAsync(Event evt)
    {
        if (!MatchesFilter(evt)) return;

        var eventRecord = ConvertEventToRecord(evt);
        var header = new ResponseHeader { Seq = _seq, Error = "" };
        
        if (_client is IpcClientHandler handler)
        {
            await handler.SendAsync(header, eventRecord, _cancellationToken);
        }
    }

    /// <summary>
    /// Converts an Event to wire protocol format.
    /// Matches Go's event record structures.
    /// </summary>
    public object ConvertEventToRecord(Event evt)
    {
        var record = new Dictionary<string, object>();

        switch (evt)
        {
            case MemberEvent me:
                record["Event"] = GetEventType(me);
                record["Members"] = me.Members ?? new List<Serf.Member>();
                break;

            case UserEvent ue:
                record["Event"] = "user";
                record["LTime"] = ue.LTime;
                record["Name"] = ue.Name;
                record["Payload"] = ue.Payload ?? Array.Empty<byte>();
                record["Coalesce"] = ue.Coalesce;
                break;

            case Query q:
                record["Event"] = "query";
                record["LTime"] = q.LTime;
                record["Name"] = q.Name;
                record["Payload"] = q.Payload ?? Array.Empty<byte>();
                break;

            default:
                record["Event"] = "unknown";
                break;
        }

        return record;
    }

    /// <summary>
    /// Gets the event type string for an event.
    /// Matches Go's event type names.
    /// </summary>
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

    /// <summary>
    /// Stops this event stream.
    /// </summary>
    public void Stop()
    {
        // Stream is stopped via cancellation token
        // No additional cleanup needed
    }
}
