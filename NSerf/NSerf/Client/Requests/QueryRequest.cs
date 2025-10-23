// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class QueryRequest
{
    [Key(0)]
    public string FilterNodes { get; set; } = string.Empty;
    
    [Key(1)]
    public string FilterTags { get; set; } = string.Empty;
    
    [Key(2)]
    public bool RequestAck { get; set; }
    
    [Key(3)]
    public uint Timeout { get; set; }
    
    [Key(4)]
    public string Name { get; set; } = string.Empty;
    
    [Key(5)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}
