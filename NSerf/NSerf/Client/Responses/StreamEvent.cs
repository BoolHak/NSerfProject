// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Responses;

[MessagePackObject]
public class StreamEvent
{
    [Key(0)]
    public string Event { get; set; } = string.Empty;
    
    [Key(1)]
    public ulong LTime { get; set; }
    
    [Key(2)]
    public string Name { get; set; } = string.Empty;
    
    [Key(3)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    
    [Key(4)]
    public Member[] Members { get; set; } = Array.Empty<Member>();
    
    [Key(5)]
    public string Type { get; set; } = string.Empty;
    
    [Key(6)]
    public ulong QueryID { get; set; }
    
    [Key(7)]
    public string From { get; set; } = string.Empty;
}
