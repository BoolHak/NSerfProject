// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class CoordinateRequest
{
    [Key(0)]
    public string Node { get; set; } = string.Empty;
}
