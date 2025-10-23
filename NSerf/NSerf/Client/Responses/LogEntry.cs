// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Responses;

[MessagePackObject]
public class LogEntry
{
    [Key(0)]
    public string Log { get; set; } = string.Empty;
}
