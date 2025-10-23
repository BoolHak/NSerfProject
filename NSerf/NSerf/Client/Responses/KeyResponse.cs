// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Responses;

[MessagePackObject]
public class KeyResponse
{
    [Key(0)]
    public int NumNodes { get; set; }
    
    [Key(1)]
    public int NumErr { get; set; }
    
    [Key(2)]
    public int NumResp { get; set; }
    
    [Key(3)]
    public Dictionary<string, int> Keys { get; set; } = new();
    
    [Key(4)]
    public string[] Messages { get; set; } = Array.Empty<string>();
    
    [Key(5)]
    public string PrimaryKey { get; set; } = string.Empty;
}
