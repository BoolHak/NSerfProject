// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

/// <summary>
/// Join command - joins a Serf cluster.
/// </summary>
public static class JoinCommand
{
    public static Command Create()
    {
        var command = new Command("join", "Tell Serf agent to join cluster");

        var replayOption = new Option<bool>("--replay")
        {
            Description = "Replay past user events"
        };

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

        var addressesArgument = new Argument<string[]>("addresses")
        {
            Description = "Addresses to join",
            Arity = ArgumentArity.ZeroOrMore
        };

        command.Add(replayOption);
        command.Add(rpcAddrOption);
        command.Add(rpcAuthOption);
        command.Add(addressesArgument);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var replay = parseResult.GetValue(replayOption);
            var rpcAddr = parseResult.GetValue(rpcAddrOption)!;
            var rpcAuth = parseResult.GetValue(rpcAuthOption);
            var addresses = parseResult.GetValue(addressesArgument) ?? [];

            if (addresses.Length == 0)
            {
                await Console.Error.WriteLineAsync("Error: At least one address to join must be specified.");
                return 1;
            }

            try
            {
                await ExecuteAsync(rpcAddr, rpcAuth, addresses, replay, cancellationToken);
                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error connecting to Serf agent: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string rpcAddr,
        string? rpcAuth,
        string[] addresses,
        bool replay,
        CancellationToken cancellationToken)
    {
        await using var client = await RpcHelper.ConnectAsync(rpcAddr, rpcAuth, cancellationToken);

        var n = await client.JoinAsync(addresses, replay, cancellationToken);
        Console.WriteLine($"Successfully joined cluster by contacting {n} nodes.");
    }
}
