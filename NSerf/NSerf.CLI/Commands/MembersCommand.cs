// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Helpers;
using NSerf.Client.Responses;

namespace NSerf.CLI.Commands;

/// <summary>
/// Members command - lists cluster members.
/// </summary>
public static class MembersCommand
{
    public static Command Create()
    {
        var command = new Command("members", "List cluster members");

        // Options
        var detailedOption = new Option<bool>("--detailed")
        {
            Description = "Show detailed information (protocol versions)"
        };

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format (text or json)",
            DefaultValueFactory = _ => "text"
        };

        var nameOption = new Option<string?>("--name")
        {
            Description = "Filter members by name (regex)"
        };

        var statusOption = new Option<string?>("--status")
        {
            Description = "Filter members by status (regex)"
        };

        var tagOption = new Option<string[]>("--tag")
        {
            Description = "Filter members by tag (key=regex)",
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

        command.Add(detailedOption);
        command.Add(formatOption);
        command.Add(nameOption);
        command.Add(statusOption);
        command.Add(tagOption);
        command.Add(rpcAddrOption);
        command.Add(rpcAuthOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var detailed = parseResult.GetValue(detailedOption);
            var format = parseResult.GetValue(formatOption)!;
            var name = parseResult.GetValue(nameOption);
            var status = parseResult.GetValue(statusOption);
            var tags = parseResult.GetValue(tagOption) ?? [];
            var rpcAddr = parseResult.GetValue(rpcAddrOption)!;
            var rpcAuth = parseResult.GetValue(rpcAuthOption);

            try
            {
                var outputFormat = OutputFormatter.ParseFormat(format);
                await ExecuteAsync(
                    rpcAddr,
                    rpcAuth,
                    name,
                    status,
                    tags,
                    outputFormat,
                    detailed,
                    cancellationToken);
                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string rpcAddr,
        string? rpcAuth,
        string? nameFilter,
        string? statusFilter,
        string[] tagFilters,
        OutputFormatter.OutputFormat format,
        bool detailed,
        CancellationToken cancellationToken)
    {
        await using var client = await RpcHelper.ConnectAsync(rpcAddr, rpcAuth, cancellationToken);

        var tags = ParseTagFilters(tagFilters);
        
        Member[] members;
        if (tags.Count > 0 || !string.IsNullOrEmpty(statusFilter) || !string.IsNullOrEmpty(nameFilter))
        {
            members = await client.MembersFilteredAsync(
                tags, 
                statusFilter ?? string.Empty, 
                nameFilter ?? string.Empty, 
                cancellationToken);
        }
        else
        {
            members = await client.MembersAsync(cancellationToken);
        }

        OutputFormatter.FormatMembers(members, format, detailed);
    }

    private static Dictionary<string, string> ParseTagFilters(string[] tagFilters)
    {
        var tags = new Dictionary<string, string>();

        foreach (var filter in tagFilters)
        {
            var parts = filter.Split('=', 2);
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid tag filter format: {filter}. Expected key=regex");
            }

            tags[parts[0]] = parts[1];
        }

        return tags;
    }
}
