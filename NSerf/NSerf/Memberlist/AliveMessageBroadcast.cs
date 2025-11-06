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

    public bool Invalidates(IBroadcast other) => other is SuspectMessageBroadcast suspect && suspect.Node == Node;

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

    public byte[] Message() => _message;

    public void Finished()
    {
        _notify?.Notify();
    }
}
