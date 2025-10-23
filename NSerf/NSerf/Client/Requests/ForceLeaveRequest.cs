// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class ForceLeaveRequest
{
    [Key(0)]
    public string Node { get; set; } = string.Empty;
    
    [Key(1)]
    public bool Prune { get; set; }
}
