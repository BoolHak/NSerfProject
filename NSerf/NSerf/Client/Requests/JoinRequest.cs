// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class JoinRequest
{
    [Key(0)]
    public string[] Existing { get; set; } = [];
    
    [Key(1)]
    public bool Replay { get; set; }
}
