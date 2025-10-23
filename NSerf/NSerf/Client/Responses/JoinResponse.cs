// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Responses;

[MessagePackObject]
public class JoinResponse
{
    [Key(0)]
    public int Num { get; set; }
}
