// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class TagsRequest
{
    [Key(0)]
    public Dictionary<string, string> Tags { get; set; } = new();
    
    [Key(1)]
    public string[] DeleteTags { get; set; } = Array.Empty<string>();
}
