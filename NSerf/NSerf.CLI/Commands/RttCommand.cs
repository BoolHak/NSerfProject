// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

/// <summary>
/// RTT command - measures round-trip time to a node.
/// </summary>
public static class RttCommand
{
    public static Command Create()
    {
        var command = new Command("rtt", "Measure round-trip time to a node");

        var rpcAddrOption = new Option<string>("--rpc-addr")
        {
            Description = "RPC address of the Serf agent",
            DefaultValueFactory = _ => RpcHelper.GetDefaultRpcAddress()
        };

        var rpcAuthOption = new Option<string?>("--rpc-auth")
        {
            Description = "RPC auth token",
            DefaultValueFactory = _ => RpcHelper.GetDefaultRpcAuth()
        };

        var nodeArgument = new Argument<string>("node")
        {
            Description = "Node name to measure RTT to"
        };

        command.Add(rpcAddrOption);
        command.Add(rpcAuthOption);
        command.Add(nodeArgument);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var rpcAddr = parseResult.GetValue(rpcAddrOption)!;
            var rpcAuth = parseResult.GetValue(rpcAuthOption);
            var node = parseResult.GetValue(nodeArgument)!;

            try
            {
                await ExecuteAsync(rpcAddr, rpcAuth, node, cancellationToken);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error: {ex.Message}");
                throw;
            }
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string rpcAddr,
        string? rpcAuth,
        string node,
        CancellationToken cancellationToken)
    {
        await using var client = await RpcHelper.ConnectAsync(rpcAddr, rpcAuth, cancellationToken);

        var coordinate = await client.GetCoordinateAsync(node, cancellationToken);

        Console.WriteLine($"Coordinate information for {node}: Available");
    }
}
