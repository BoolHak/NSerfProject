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
    private readonly string _node;
    private readonly byte[] _message;
    
    public AliveMessageBroadcast(string node, byte[] message)
    {
        _node = node;
        _message = message;
    }
    
    public bool Invalidates(IBroadcast other)
    {
        if (other is AliveMessageBroadcast alive && alive._node == _node)
        {
            return true;
        }
        else if (other is SuspectMessageBroadcast suspect && suspect.Node == _node)
        {
            return true;
        }
        else if (other is DeadMessageBroadcast dead && dead.Node == _node)
        {
            return true;
        }
        return false;
    }
    
    public byte[] Message() => _message;
    public void Finished() { }
}

/// <summary>
/// Broadcast for suspect messages.
/// </summary>
public class SuspectMessageBroadcast : IBroadcast
{
    private readonly byte[] _message;
    
    public string Node { get; }
    
    public SuspectMessageBroadcast(string node, byte[] message)
    {
        Node = node;
        _message = message;
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
    public void Finished() { }
}

/// <summary>
/// Broadcast for dead messages.
/// </summary>
public class DeadMessageBroadcast : IBroadcast
{
    private readonly byte[] _message;
    
    public string Node { get; }
    
    public DeadMessageBroadcast(string node, byte[] message)
    {
        Node = node;
        _message = message;
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
        return false;
    }
    
    public byte[] Message() => _message;
    public void Finished() { }
}
