// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using System.Text;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

/// <summary>
/// Event command - dispatches a custom user event.
/// </summary>
public static class EventCommand
{
    public static Command Create()
    {
        var command = new Command("event", "Dispatch a custom event through the Serf cluster");

        var coalesceOption = new Option<bool>("--coalesce")
        {
            Description = "Whether this event can be coalesced",
            DefaultValueFactory = _ => true
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

        var nameArgument = new Argument<string>("name")
        {
            Description = "Event name"
        };

        var payloadArgument = new Argument<string?>("payload")
        {
            Description = "Event payload (optional)",
            Arity = ArgumentArity.ZeroOrOne
        };

        command.Add(coalesceOption);
        command.Add(rpcAddrOption);
        command.Add(rpcAuthOption);
        command.Add(nameArgument);
        command.Add(payloadArgument);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var coalesce = parseResult.GetValue(coalesceOption);
            var rpcAddr = parseResult.GetValue(rpcAddrOption)!;
            var rpcAuth = parseResult.GetValue(rpcAuthOption);
            var name = parseResult.GetValue(nameArgument)!;
            var payload = parseResult.GetValue(payloadArgument);

            try
            {
                await ExecuteAsync(rpcAddr, rpcAuth, name, payload, coalesce, cancellationToken);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string rpcAddr,
        string? rpcAuth,
        string name,
        string? payload,
        bool coalesce,
        CancellationToken cancellationToken)
    {
        using var client = await RpcHelper.ConnectAsync(rpcAddr, rpcAuth, cancellationToken);

        byte[]? payloadBytes = null;
        if (!string.IsNullOrEmpty(payload))
        {
            payloadBytes = Encoding.UTF8.GetBytes(payload);
        }

        await client.UserEventAsync(name, payloadBytes, coalesce, cancellationToken);
        Console.WriteLine($"Event '{name}' dispatched! Coalescing enabled: {coalesce}");
    }
}
