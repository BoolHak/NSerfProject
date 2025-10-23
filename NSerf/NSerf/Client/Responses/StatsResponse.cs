// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Responses;

[MessagePackObject]
public class StatsResponse
{
    [Key(0)]
    public Dictionary<string, Dictionary<string, string>> Stats { get; set; } = new();
}
