// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Client;

/// <summary>
/// Configuration for RPC client connection.
/// Maps to: Go's Config in client/rpc_client.go
/// </summary>
public class RpcConfig
{
    /// <summary>
    /// Address of the Serf agent RPC endpoint (format: "host:port")
    /// </summary>
    public string Address { get; set; } = "127.0.0.1:7373";

    /// <summary>
    /// Authentication key for the RPC connection (optional)
    /// </summary>
    public string? AuthKey { get; set; }

    /// <summary>
    /// Timeout for RPC operations
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
