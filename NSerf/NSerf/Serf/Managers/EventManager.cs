// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Serf.Events;
using NSerf.Serf.Helpers;
using System.Threading.Channels;

namespace NSerf.Serf.Managers;

/// <summary>
/// Manages user events: buffering, deduplication, and emission.
/// Reference: Go serf/serf.go handleUserEvent() and event management
/// </summary>
public class EventManager
{
    private readonly ChannelWriter<Event>? _eventCh;
    private readonly int _eventBufferSize;
    private readonly ILogger? _logger;

    // Event buffer for deduplication (circular buffer indexed by LTime % bufferSize)
    private readonly Dictionary<LamportTime, UserEventCollection> _eventBuffer;

    // Event clock for logical time ordering
    private LamportTime _eventClockTime;

    // Minimum event time (events below this are ignored)
    private LamportTime _eventMinTime;

    // Lock for thread-safe access to event state
    private readonly ReaderWriterLockSlim _eventLock = new();

    /// <summary>
    /// Creates a new EventManager.
    /// </summary>
    /// <param name="eventCh">Optional channel to emit events to</param>
    /// <param name="eventBufferSize">Size of the event deduplication buffer</param>
    /// <param name="logger">Optional logger</param>
    public EventManager(
        ChannelWriter<Event>? eventCh,
        int eventBufferSize,
        ILogger? logger = null)
    {
        _eventCh = eventCh;
        _eventBufferSize = eventBufferSize;
        _logger = logger;
        _eventBuffer = [];
        _eventClockTime = 0;
        _eventMinTime = 0;
    }

    /// <summary>
    /// Handles a user event message. Performs deduplication, buffering, and emission.
    /// Returns true if the event should be rebroadcast, false if it's a duplicate or should be ignored.
    /// Reference: Go serf/serf.go handleUserEvent()
    /// </summary>
    public bool HandleUserEvent(MessageUserEvent userEvent)
    {
        // Witness a potentially newer time
        WitnessEventClock(userEvent.LTime);

        return LockHelper.WithWriteLock(_eventLock, () =>
        {
            // Ignore if it is before our minimum event time
            if (userEvent.LTime < _eventMinTime)
            {
                _logger?.LogTrace("[EventManager] Ignoring event {Name} at LTime {LTime} (below minTime {MinTime})",
                    userEvent.Name, userEvent.LTime, _eventMinTime);
                return false;
            }

            // Check if this message is too old
            var curTime = _eventClockTime;
            var bufferSize = (ulong)_eventBufferSize;
            if (curTime > bufferSize && userEvent.LTime < (curTime - bufferSize))
            {
                _logger?.LogWarning(
                    "[EventManager] Received old event {Name} from time {LTime} (current: {CurTime})",
                    userEvent.Name, userEvent.LTime, curTime);
                return false;
            }

            // Check if we've already seen this event (deduplication)
            if (_eventBuffer.TryGetValue(userEvent.LTime, out var seen))
            {
                // Check for duplicate
                foreach (var previous in seen.Events)
                {
                    if (previous.Name == userEvent.Name &&
                        previous.Payload.SequenceEqual(userEvent.Payload))
                    {
                        // Already seen this event
                        _logger?.LogTrace("[EventManager] Duplicate event {Name} at LTime {LTime}",
                            userEvent.Name, userEvent.LTime);
                        return false;
                    }
                }
            }
            else
            {
                // Create new collection for this LTime
                seen = new UserEventCollection { LTime = userEvent.LTime };
                _eventBuffer[userEvent.LTime] = seen;
            }

            // Add to recent events
            seen.Events.Add(new UserEventData
            {
                Name = userEvent.Name,
                Payload = userEvent.Payload
            });

            _logger?.LogDebug("[EventManager] Processing new event {Name} at LTime {LTime}",
                userEvent.Name, userEvent.LTime);

            // Send to EventCh if configured
            if (_eventCh != null)
            {
                var evt = new Events.UserEvent
                {
                    LTime = userEvent.LTime,
                    Name = userEvent.Name,
                    Payload = userEvent.Payload,
                    Coalesce = userEvent.CC
                };

                try
                {
                    _eventCh.TryWrite(evt);
                    _logger?.LogTrace("[EventManager] Emitted UserEvent: {Name} at LTime {LTime}",
                        userEvent.Name, userEvent.LTime);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[EventManager] Failed to emit UserEvent: {Name}", userEvent.Name);
                }
            }

            return true; // Rebroadcast this event
        });
    }

    /// <summary>
    /// Emits an event to the event channel (for system events like member join/leave).
    /// This method does NOT use the event buffer or deduplication logic.
    /// </summary>
    public void EmitEvent(Event evt)
    {
        _logger?.LogDebug("[EventManager] Emitting {EventType}", evt.GetType().Name);

        // Send to event channel if configured
        if (_eventCh != null)
        {
            try
            {
                _eventCh.TryWrite(evt);
                _logger?.LogTrace("[EventManager] Emitted event to EventCh: {Type}", evt.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[EventManager] Failed to emit event to EventCh");
            }
        }
    }

    /// <summary>
    /// Witnesses a Lamport time value, updating the event clock if necessary.
    /// Thread-safe operation.
    /// </summary>
    public void WitnessEventClock(LamportTime time)
    {
        LockHelper.WithWriteLock(_eventLock, () =>
        {
            if (time > _eventClockTime)
            {
                _eventClockTime = time;
            }
        });
    }

    /// <summary>
    /// Gets the current event clock time.
    /// Thread-safe operation.
    /// </summary>
    public LamportTime GetEventClockTime()
    {
        return LockHelper.WithReadLock(_eventLock, () => _eventClockTime);
    }

    /// <summary>
    /// Sets the minimum event time. Events with LTime below this will be ignored.
    /// Used during join operations to ignore old events.
    /// Thread-safe operation.
    /// </summary>
    public void SetEventMinTime(LamportTime minTime)
    {
        LockHelper.WithWriteLock(_eventLock, () =>
        {
            _eventMinTime = minTime;
            _logger?.LogDebug("[EventManager] Set eventMinTime to {MinTime}", minTime);
        });
    }

    /// <summary>
    /// Gets the current minimum event time.
    /// Thread-safe operation.
    /// </summary>
    public LamportTime GetEventMinTime()
    {
        return LockHelper.WithReadLock(_eventLock, () => _eventMinTime);
    }

    /// <summary>
    /// Gets all event collections for push/pull state synchronization.
    /// Returns a snapshot of the current event buffer.
    /// Thread-safe operation.
    /// </summary>
    public List<UserEventCollection> GetEventCollectionsForPushPull()
    {
        return LockHelper.WithReadLock(_eventLock, () =>
            _eventBuffer.Values.ToList());
    }

    /// <summary>
    /// Disposes the EventManager and releases locks.
    /// </summary>
    public void Dispose()
    {
        _eventLock?.Dispose();
    }
}
