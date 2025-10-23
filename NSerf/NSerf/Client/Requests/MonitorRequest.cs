// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class MonitorRequest
{
    [Key(0)]
    public string LogLevel { get; set; } = "INFO";
}
