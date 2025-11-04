// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using MessagePack;

namespace NSerf.Client.Responses;

[MessagePackObject]
public class Member
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;
    
    [Key(1)]
    public byte[] Addr { get; set; } = [];
    
    [Key(2)]
    public ushort Port { get; set; }
    
    [Key(3)]
    public Dictionary<string, string> Tags { get; set; } = new();
    
    [Key(4)]
    public string Status { get; set; } = string.Empty;
    
    [Key(5)]
    public byte ProtocolMin { get; set; }
    
    [Key(6)]
    public byte ProtocolMax { get; set; }
    
    [Key(7)]
    public byte ProtocolCur { get; set; }
    
    [Key(8)]
    public byte DelegateMin { get; set; }
    
    [Key(9)]
    public byte DelegateMax { get; set; }
    
    [Key(10)]
    public byte DelegateCur { get; set; }
    
    [IgnoreMember]
    public IPAddress? Address => Addr.Length > 0 ? new IPAddress(Addr) : null;
}
