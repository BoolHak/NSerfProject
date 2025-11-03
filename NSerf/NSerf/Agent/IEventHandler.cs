// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Serf.Events;

namespace NSerf.Agent;

/// <summary>
/// Interface for handling Serf events in the agent.
/// Handlers are registered with the agent and receive all events from the Serf cluster.
/// </summary>
public interface IEventHandler
{
    /// <summary>
    /// Handle a Serf event.
    /// This method should not block for extended periods.
    /// Exceptions thrown by this method will be logged but will not stop the event loop.
    /// </summary>
    /// <param name="event">The Serf event to handle</param>
    void HandleEvent(IEvent @event);
}
