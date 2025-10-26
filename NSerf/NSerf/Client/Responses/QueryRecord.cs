// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client.Responses;

/// <summary>
/// QueryRecord represents a single streaming record for a query (ack, response, or done).
/// Maps to: Go's queryRecord in const.go
/// </summary>
[MessagePackObject]
public class QueryRecord
{
    [Key(0)]
    public string Type { get; set; } = string.Empty;

    [Key(1)]
    public string From { get; set; } = string.Empty;

    [Key(2)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Query record types
/// </summary>
public static class QueryRecordType
{
    public const string Ack = "ack";
    public const string Response = "response";
    public const string Done = "done";
}

/// <summary>
/// Node response from a query
/// </summary>
public class NodeResponse
{
    public string From { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}
