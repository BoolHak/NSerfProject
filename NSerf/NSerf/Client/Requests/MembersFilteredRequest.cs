// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class MembersFilteredRequest
{
    [Key(0)]
    public Dictionary<string, string> Tags { get; set; } = new();
    
    [Key(1)]
    public string Status { get; set; } = string.Empty;
    
    [Key(2)]
    public string Name { get; set; } = string.Empty;
}
