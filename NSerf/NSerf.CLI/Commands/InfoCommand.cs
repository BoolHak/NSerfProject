// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

/// <summary>
/// Info command - displays agent runtime information.
/// </summary>
public static class InfoCommand
{
    public static Command Create()
    {
        var command = new Command("info", "Show runtime information about the agent");

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format (text or json)",
            DefaultValueFactory = _ => "text"
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

        command.Add(formatOption);
        command.Add(rpcAddrOption);
        command.Add(rpcAuthOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var rpcAddr = parseResult.GetValue(rpcAddrOption)!;
            var rpcAuth = parseResult.GetValue(rpcAuthOption);

            try
            {
                var outputFormat = OutputFormatter.ParseFormat(format);
                await ExecuteAsync(rpcAddr, rpcAuth, outputFormat, cancellationToken);
                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error querying agent: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string rpcAddr,
        string? rpcAuth,
        OutputFormatter.OutputFormat format,
        CancellationToken cancellationToken)
    {
        await using var client = await RpcHelper.ConnectAsync(rpcAddr, rpcAuth, cancellationToken);

        var statsDict = await client.StatsAsync(cancellationToken);

        if (format == OutputFormatter.OutputFormat.Json)
        {
            OutputFormatter.FormatJson(statsDict);
        }
        else
        {
            foreach (var section in statsDict.OrderBy(s => s.Key))
            {
                Console.WriteLine($"{section.Key}:");
                foreach (var stat in section.Value.OrderBy(v => v.Key))
                {
                    Console.WriteLine($"\t{stat.Key} = {stat.Value}");
                }
            }
        }
    }
}
