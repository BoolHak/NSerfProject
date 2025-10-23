// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Requests;

[MessagePackObject]
public class KeyRequest
{
    [Key(0)]
    public string Key { get; set; } = string.Empty;
}
