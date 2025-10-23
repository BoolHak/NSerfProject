// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class EventRequest
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;
    
    [Key(1)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    
    [Key(2)]
    public bool Coalesce { get; set; }
}
