// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Broadcast for alive messages.
/// </summary>
public class AliveMessageBroadcast(string node, byte[] message, BroadcastNotifyChannel? notify = null) : IBroadcast
{
    private readonly byte[] _message = message;
    private readonly BroadcastNotifyChannel? _notify = notify;

    public string Node { get; } = node;

    public bool Invalidates(IBroadcast other)
    {
        if (other is AliveMessageBroadcast alive && alive.Node == Node)
        {
            return true;
        }
        else if (other is SuspectMessageBroadcast suspect && suspect.Node == Node)
        {
            return true;
        }
        else if (other is DeadMessageBroadcast dead && dead.Node == Node)
        {
            return true;
        }
        return false;
    }

    public byte[] Message() => _message;

    public void Finished()
    {
        _notify?.Notify();
    }
}

/// <summary>
/// Broadcast for suspect messages.
/// </summary>
public class SuspectMessageBroadcast(string node, byte[] message, BroadcastNotifyChannel? notify = null) : IBroadcast
{
    private readonly byte[] _message = message;
    private readonly BroadcastNotifyChannel? _notify = notify;

    public string Node { get; } = node;

    public bool Invalidates(IBroadcast other)
    {
        if (other is SuspectMessageBroadcast suspect && suspect.Node == Node)
        {
            return true;
        }
        return false;
    }

    public byte[] Message() => _message;

    public void Finished()
    {
        _notify?.Notify();
    }
}

/// <summary>
/// Broadcast for dead messages.
/// </summary>
public class DeadMessageBroadcast(string node, byte[] message, BroadcastNotifyChannel? notify = null) : IBroadcast
{
    private readonly byte[] _message = message;
    private readonly BroadcastNotifyChannel? _notify = notify;

    public string Node { get; } = node;

    public bool Invalidates(IBroadcast other)
    {
        if (other is DeadMessageBroadcast dead && dead.Node == Node)
        {
            return true;
        }
        else if (other is SuspectMessageBroadcast suspect && suspect.Node == Node)
        {
            return true;
        }
        else if (other is AliveMessageBroadcast alive && alive.Node == Node)
        {
            // Dead broadcast invalidates Alive broadcast for the same node
            return true;
        }
        return false;
    }

    public byte[] Message() => _message;

    public void Finished()
    {
        _notify?.Notify();
    }
}
