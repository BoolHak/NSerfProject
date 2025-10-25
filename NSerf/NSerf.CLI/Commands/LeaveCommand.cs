// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

/// <summary>
/// Leave command - gracefully leaves the Serf cluster.
/// </summary>
public static class LeaveCommand
{
    public static Command Create()
    {
        var command = new Command("leave", "Gracefully leave the Serf cluster");

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

        command.Add(rpcAddrOption);
        command.Add(rpcAuthOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var rpcAddr = parseResult.GetValue(rpcAddrOption)!;
            var rpcAuth = parseResult.GetValue(rpcAuthOption);

            try
            {
                await ExecuteAsync(rpcAddr, rpcAuth, cancellationToken);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error connecting to Serf agent: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string rpcAddr,
        string? rpcAuth,
        CancellationToken cancellationToken)
    {
        using var client = await RpcHelper.ConnectAsync(rpcAddr, rpcAuth, cancellationToken);

        await client.LeaveAsync(cancellationToken);
        Console.WriteLine("Graceful leave complete");
    }
}
