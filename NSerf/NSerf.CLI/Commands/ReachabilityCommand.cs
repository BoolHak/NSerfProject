// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

public static class ReachabilityCommand
{
    public static Command Create()
    {
        var command = new Command("reachability", "Test reachability to a node");

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
            Description = "Node name to test reachability"
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
                Console.Error.WriteLine($"Error: {ex.Message}");
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
        using var client = await RpcHelper.ConnectAsync(rpcAddr, rpcAuth, cancellationToken);

        var members = await client.MembersAsync(cancellationToken);
        var member = members.FirstOrDefault(m => m.Name == node);

        if (member != null)
        {
            Console.WriteLine($"{node} is {member.Status}");
        }
        else
        {
            Console.WriteLine($"{node} not found in cluster");
        }
    }
}
