// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Broadcast for alive messages.
/// </summary>
public class AliveMessageBroadcast : IBroadcast
{
    private readonly byte[] _message;
    private readonly BroadcastNotifyChannel? _notify;
    
    public string Node { get; }
    
    public AliveMessageBroadcast(string node, byte[] message, BroadcastNotifyChannel? notify = null)
    {
        Node = node;
        _message = message;
        _notify = notify;
    }
    
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
public class SuspectMessageBroadcast : IBroadcast
{
    private readonly byte[] _message;
    private readonly BroadcastNotifyChannel? _notify;
    
    public string Node { get; }
    
    public SuspectMessageBroadcast(string node, byte[] message, BroadcastNotifyChannel? notify = null)
    {
        Node = node;
        _message = message;
        _notify = notify;
    }
    
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
public class DeadMessageBroadcast : IBroadcast
{
    private readonly byte[] _message;
    private readonly BroadcastNotifyChannel? _notify;
    
    public string Node { get; }
    
    public DeadMessageBroadcast(string node, byte[] message, BroadcastNotifyChannel? notify = null)
    {
        Node = node;
        _message = message;
        _notify = notify;
    }
    
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
