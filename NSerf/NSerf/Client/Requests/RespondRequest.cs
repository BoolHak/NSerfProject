// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class RespondRequest
{
    [Key(0)]
    public ulong ID { get; set; }
    
    [Key(1)]
    public byte[] Payload { get; set; } = [];
}
