// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

/// <summary>
/// Tags command - modify tags on the local agent.
/// </summary>
public static class TagsCommand
{
    public static Command Create()
    {
        var command = new Command("tags", "Modify tags on the agent");

        var setOption = new Option<string[]>("--set")
        {
            Description = "Tags to set or update (key=value)",
            AllowMultipleArgumentsPerToken = true
        };

        var deleteOption = new Option<string[]>("--delete")
        {
            Description = "Tags to delete (key)",
            AllowMultipleArgumentsPerToken = true
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

        command.Add(setOption);
        command.Add(deleteOption);
        command.Add(rpcAddrOption);
        command.Add(rpcAuthOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var setTags = parseResult.GetValue(setOption) ?? [];
            var deleteTags = parseResult.GetValue(deleteOption) ?? [];
            var rpcAddr = parseResult.GetValue(rpcAddrOption)!;
            var rpcAuth = parseResult.GetValue(rpcAuthOption);

            if (setTags.Length == 0 && deleteTags.Length == 0)
            {
                await Console.Error.WriteLineAsync("Error: At least one --set or --delete operation must be specified");
                return 1;
            }

            try
            {
                await ExecuteAsync(rpcAddr, rpcAuth, setTags, deleteTags, cancellationToken);
                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error setting tags: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string rpcAddr,
        string? rpcAuth,
        string[] setTags,
        string[] deleteTags,
        CancellationToken cancellationToken)
    {
        await using var client = await RpcHelper.ConnectAsync(rpcAddr, rpcAuth, cancellationToken);

        var tagsToSet = new Dictionary<string, string>();
        foreach (var tag in setTags)
        {
            var parts = tag.Split('=', 2);
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid tag format: {tag}. Expected key=value");
            }
            tagsToSet[parts[0]] = parts[1];
        }

        await client.UpdateTagsAsync(tagsToSet, deleteTags, cancellationToken);
        Console.WriteLine("Successfully updated agent tags");
    }
}
