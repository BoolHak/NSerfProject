// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

/// <summary>
/// Force-leave command - forces a node to leave the cluster.
/// </summary>
public static class ForceLeaveCommand
{
    public static Command Create()
    {
        var command = new Command("force-leave", "Force a node to leave the cluster");

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
            Description = "Name of the node to force leave"
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

        await client.ForceLeaveAsync(node, false, cancellationToken);

        Console.WriteLine($"Force leave request sent for node: {node}");
    }
}
