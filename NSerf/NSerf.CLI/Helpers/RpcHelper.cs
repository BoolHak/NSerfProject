// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Client;

namespace NSerf.CLI.Helpers;

/// <summary>
/// Helper for creating and managing RPC client connections.
/// </summary>
public static class RpcHelper
{
    /// <summary>
    /// Gets the default RPC address from environment or default value.
    /// </summary>
    public static string GetDefaultRpcAddress()
    {
        return Environment.GetEnvironmentVariable("SERF_RPC_ADDR") ?? "127.0.0.1:7373";
    }

    /// <summary>
    /// Gets the default RPC auth key from the environment.
    /// </summary>
    public static string? GetDefaultRpcAuth()
    {
        return Environment.GetEnvironmentVariable("SERF_RPC_AUTH");
    }

    /// <summary>
    /// Creates an RPC client with the specified address and auth key.
    /// </summary>
    public static async Task<RpcClient> ConnectAsync(
        string? rpcAddr = null,
        string? rpcAuth = null,
        CancellationToken cancellationToken = default)
    {
        var address = rpcAddr ?? GetDefaultRpcAddress();
        var authKey = rpcAuth ?? GetDefaultRpcAuth();

        var config = new RpcConfig
        {
            Address = address,
            AuthKey = authKey
        };

        var client = new RpcClient(config);
        
        try
        {
            await client.ConnectAsync(cancellationToken);
            return client;
        }
        catch (Exception ex)
        {
            await client.DisposeAsync();
            throw new InvalidOperationException(
                $"Failed to connect to Serf agent at {address}: {ex.Message}", ex);
        }
    }
}
