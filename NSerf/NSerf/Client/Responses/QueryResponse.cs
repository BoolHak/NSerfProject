// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Responses;

[MessagePackObject]
public class QueryResponse
{
    [Key(0)]
    public uint Id { get; set; }
}
