// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

/// <summary>
/// Monitor command - streams logs from the Serf agent.
/// </summary>
public static class MonitorCommand
{
    public static Command Create()
    {
        var command = new Command("monitor", "Stream logs from the Serf agent");

        var logLevelOption = new Option<string>("--log-level")
        {
            Description = "Log level to stream (TRACE, DEBUG, INFO, WARN, ERR)",
            DefaultValueFactory = _ => "INFO"
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

        command.Add(logLevelOption);
        command.Add(rpcAddrOption);
        command.Add(rpcAuthOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var logLevel = parseResult.GetValue(logLevelOption)!;
            var rpcAddr = parseResult.GetValue(rpcAddrOption)!;
            var rpcAuth = parseResult.GetValue(rpcAuthOption);

            try
            {
                await ExecuteAsync(rpcAddr, rpcAuth, logLevel, cancellationToken);
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
        string logLevel,
        CancellationToken cancellationToken)
    {
        using var client = await RpcHelper.ConnectAsync(rpcAddr, rpcAuth, cancellationToken);

        Console.WriteLine($"Streaming logs at level: {logLevel}");

        await foreach (var log in client.MonitorAsync(logLevel, cancellationToken))
        {
            Console.WriteLine(log);
        }
    }
}
