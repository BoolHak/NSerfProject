// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Events;

/// <summary>
/// Base interface for all Serf events.
/// Minimal implementation for Phase 0 - will be expanded in Phase 3.
/// </summary>
public interface Event
{
    /// <summary>
    /// Gets the type of this event.
    /// </summary>
    EventType EventType();

    /// <summary>
    /// Gets a string representation of this event.
    /// </summary>
    string ToString();
}
