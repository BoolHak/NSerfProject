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
/// <remarks>
/// Creates a new EventManager.
/// </remarks>
/// <param name="eventCh">Optional channel to emit events to</param>
/// <param name="eventBufferSize">Size of the event deduplication buffer</param>
/// <param name="logger">Optional logger</param>
public class EventManager(
    ChannelWriter<IEvent>? eventCh,
    int eventBufferSize,
    ILogger? logger = null)
{

    // Event buffer for deduplication (circular buffer indexed by LTime % bufferSize)
    private readonly Dictionary<LamportTime, UserEventCollection> _eventBuffer = [];

    // Event clock for logical time ordering
    private LamportTime _eventClockTime = 0;

    // Minimum event time (events below this are ignored)
    private LamportTime _eventMinTime = 0;

    // Lock for thread-safe access to the event state
    private readonly ReaderWriterLockSlim _eventLock = new();

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
                logger?.LogTrace("[EventManager] Ignoring event {Name} at LTime {LTime} (below minTime {MinTime})",
                    userEvent.Name, userEvent.LTime, _eventMinTime);
                return false;
            }

            // Check if this message is too old
            var curTime = _eventClockTime;
            var bufferSize = (ulong) eventBufferSize;
            if (curTime > bufferSize && userEvent.LTime < (curTime - bufferSize))
            {
                logger?.LogWarning(
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
                        logger?.LogTrace("[EventManager] Duplicate event {Name} at LTime {LTime}",
                            userEvent.Name, userEvent.LTime);
                        return false;
                    }
                }
            }
            else
            {
                // Create a new collection for this LTime
                seen = new UserEventCollection { LTime = userEvent.LTime };
                _eventBuffer[userEvent.LTime] = seen;
            }

            // Add to recent events
            seen.Events.Add(new UserEventData
            {
                Name = userEvent.Name,
                Payload = userEvent.Payload
            });

            logger?.LogDebug("[EventManager] Processing new event {Name} at LTime {LTime}",
                userEvent.Name, userEvent.LTime);

            // Send to EventCh if configured
            if (eventCh == null) return true; 
            
            // Rebroadcast this event
            var evt = new UserEvent
            {
                LTime = userEvent.LTime,
                Name = userEvent.Name,
                Payload = userEvent.Payload,
                Coalesce = userEvent.CC
            };

            try
            {
                eventCh.TryWrite(evt);
                logger?.LogTrace("[EventManager] Emitted UserEvent: {Name} at LTime {LTime}",
                    userEvent.Name, userEvent.LTime);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[EventManager] Failed to emit UserEvent: {Name}", userEvent.Name);
            }

            return true; // Rebroadcast this event
        });
    }

    /// <summary>
    /// Emits an event to the event channel (for system events like member join/leave).
    /// This method does NOT use the event buffer or deduplication logic.
    /// </summary>
    public void EmitEvent(IEvent evt)
    {
        logger?.LogDebug("[EventManager] Emitting {EventType}", evt.GetType().Name);

        // Send it to the event channel if configured
        if (eventCh == null) return;
        
        try
        {
            eventCh.TryWrite(evt);
            logger?.LogTrace("[EventManager] Emitted event to EventCh: {Type}", evt.GetType().Name);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[EventManager] Failed to emit event to EventCh");
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
            logger?.LogDebug("[EventManager] Set eventMinTime to {MinTime}", minTime);
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
