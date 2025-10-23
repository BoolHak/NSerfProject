// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class StreamRequest
{
    [Key(0)]
    public string Type { get; set; } = "*";
}
