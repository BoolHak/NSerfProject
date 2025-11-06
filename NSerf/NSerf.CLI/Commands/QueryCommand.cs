// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using System.Text;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

/// <summary>
/// Query command - sends a query and waits for responses.
/// </summary>
public static class QueryCommand
{
    public static Command Create()
    {
        var command = new Command("query", "Send a query and wait for responses");

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format (text or json)",
            DefaultValueFactory = _ => "text"
        };

        var noAckOption = new Option<bool>("--no-ack")
        {
            Description = "Don't require acks from nodes"
        };

        var timeoutOption = new Option<int>("--timeout")
        {
            Description = "Query timeout in seconds",
            DefaultValueFactory = _ => 5
        };

        var nodeOption = new Option<string?>("--node")
        {
            Description = "Filter by node name (regex)"
        };

        var tagOption = new Option<string[]>("--tag")
        {
            Description = "Filter by tag (key=regex)",
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

        var nameArgument = new Argument<string>("name")
        {
            Description = "Query name"
        };

        var payloadArgument = new Argument<string?>("payload")
        {
            Description = "Query payload (optional)",
            Arity = ArgumentArity.ZeroOrOne
        };

        command.Add(formatOption);
        command.Add(noAckOption);
        command.Add(timeoutOption);
        command.Add(nodeOption);
        command.Add(tagOption);
        command.Add(rpcAddrOption);
        command.Add(rpcAuthOption);
        command.Add(nameArgument);
        command.Add(payloadArgument);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var noAck = parseResult.GetValue(noAckOption);
            var timeout = parseResult.GetValue(timeoutOption);
            var node = parseResult.GetValue(nodeOption);
            var tags = parseResult.GetValue(tagOption) ?? [];
            var rpcAddr = parseResult.GetValue(rpcAddrOption)!;
            var rpcAuth = parseResult.GetValue(rpcAuthOption);
            var name = parseResult.GetValue(nameArgument)!;
            var payload = parseResult.GetValue(payloadArgument);

            try
            {
                var outputFormat = OutputFormatter.ParseFormat(format);
                await ExecuteAsync(rpcAddr, rpcAuth, name, payload, timeout, noAck, node, tags, outputFormat, cancellationToken);
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
        string name,
        string? payload,
        int timeoutSeconds,
        bool noAck,
        string? nodeFilter,
        string[] tagFilters,
        OutputFormatter.OutputFormat format,
        CancellationToken cancellationToken)
    {
        await using var client = await RpcHelper.ConnectAsync(rpcAddr, rpcAuth, cancellationToken);

        byte[]? payloadBytes = null;
        if (!string.IsNullOrEmpty(payload))
        {
            payloadBytes = Encoding.UTF8.GetBytes(payload);
        }

        var tags = new Dictionary<string, string>();
        foreach (var filter in tagFilters)
        {
            var parts = filter.Split('=', 2);
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid tag filter: {filter}. Expected key=regex");
            }
            tags[parts[0]] = parts[1];
        }

        var timeoutSecs = (uint)timeoutSeconds;
        var requestAck = !noAck;
        
        var queryId = await client.QueryAsync(
            name,
            payloadBytes,
            nodeFilter,
            tags,
            requestAck,
            timeoutSecs,
            cancellationToken);

        Console.WriteLine($"Query '{name}' dispatched with ID: {queryId}");
        
        if (requestAck)
        {
            Console.WriteLine("Waiting for acknowledgments...");
        }
    }
}
