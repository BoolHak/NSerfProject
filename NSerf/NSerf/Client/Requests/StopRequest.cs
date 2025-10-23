// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class StopRequest
{
    [Key(0)]
    public ulong Stop { get; set; }
}
