// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Broadcast;

/// <summary>
/// Broadcast for alive messages.
/// </summary>
public class AliveMessageBroadcast(string node, byte[] message, BroadcastNotifyChannel? notify = null) : IBroadcast
{
    public string Node { get; } = node;

    public bool Invalidates(IBroadcast other)
    {
        switch (other)
        {
            case AliveMessageBroadcast alive when alive.Node == Node:
            case SuspectMessageBroadcast suspect when suspect.Node == Node:
            case DeadMessageBroadcast dead when dead.Node == Node:
                return true;
            default:
                return false;
        }
    }

    public byte[] Message() => message;

    public void Finished()
    {
        notify?.Notify();
    }
}

/// <summary>
/// Broadcast for suspect messages.
/// </summary>
public class SuspectMessageBroadcast(string node, byte[] message, BroadcastNotifyChannel? notify = null) : IBroadcast
{
    public string Node { get; } = node;

    public bool Invalidates(IBroadcast other) => other is SuspectMessageBroadcast suspect && suspect.Node == Node;

    public byte[] Message() => message;

    public void Finished()
    {
        notify?.Notify();
    }
}

/// <summary>
/// Broadcast for dead messages.
/// </summary>
public class DeadMessageBroadcast(string node, byte[] message, BroadcastNotifyChannel? notify = null) : IBroadcast
{
    public string Node { get; } = node;

    public bool Invalidates(IBroadcast other)
    {
        switch (other)
        {
            case DeadMessageBroadcast dead when dead.Node == Node:
            case SuspectMessageBroadcast suspect when suspect.Node == Node:
            case AliveMessageBroadcast alive when alive.Node == Node:
                return true;
            default:
                return false;
        }
    }

    public byte[] Message() => message;

    public void Finished()
    {
        notify?.Notify();
    }
}
