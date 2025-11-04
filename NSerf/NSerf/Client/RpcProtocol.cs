// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Client;

/// <summary>
/// RPC protocol constants.
/// Maps to: Go's const.go
/// </summary>
public static class RpcConstants
{
    public const int MaxIpcVersion = 1;
}

/// <summary>
/// RPC command names.
/// Maps to: Go's const.go command constants
/// </summary>
public static class RpcCommands
{
    public const string Handshake = "handshake";
    public const string Auth = "auth";
    public const string Event = "event";
    public const string ForceLeave = "force-leave";
    public const string Join = "join";
    public const string Members = "members";
    public const string MembersFiltered = "members-filtered";
    public const string Tags = "tags";
    public const string Stream = "stream";
    public const string Monitor = "monitor";
    public const string Stop = "stop";
    public const string Leave = "leave";
    public const string Query = "query";
    public const string Respond = "respond";
    public const string InstallKey = "install-key";
    public const string UseKey = "use-key";
    public const string RemoveKey = "remove-key";
    public const string ListKeys = "list-keys";
    public const string Stats = "stats";
    public const string GetCoordinate = "get-coordinate";
}

/// <summary>
/// Request header sent before each request.
/// Maps to: Go's requestHeader in const.go
/// </summary>
[MessagePackObject]
public class RequestHeader
{
    [Key(0)]
    public string Command { get; set; } = string.Empty;

    [Key(1)]
    public ulong Seq { get; set; }
}

/// <summary>
/// Response header sent before each response.
/// Maps to: Go's responseHeader in const.go
/// </summary>
[MessagePackObject]
public class ResponseHeader
{
    [Key(0)]
    public ulong Seq { get; set; }

    [Key(1)]
    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// Handshake request.
/// Maps to: Go's handshakeRequest in const.go
/// </summary>
[MessagePackObject]
public class HandshakeRequest
{
    [Key(0)]
    public int Version { get; set; }
}

/// <summary>
/// Authentication request.
/// Maps to: Go's authRequest in const.go
/// </summary>
[MessagePackObject]
public class AuthRequest
{
    [Key(0)]
    public string AuthKey { get; set; } = string.Empty;
}

/// <summary>
/// Exception thrown for RPC errors.
/// </summary>
public class RpcException : Exception
{
    public RpcException(string message) : base(message) { }
    public RpcException(string message, Exception innerException) : base(message, innerException) { }
}
