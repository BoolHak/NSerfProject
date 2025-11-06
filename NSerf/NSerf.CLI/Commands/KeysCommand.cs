// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Helpers;

namespace NSerf.CLI.Commands;

/// <summary>
/// Keys command - manages encryption keys.
/// </summary>
public static class KeysCommand
{
    public static Command Create()
    {
        var command = new Command("keys", "Manage encryption keys");

        var listCommand = new Command("list", "List all encryption keys");
        var installCommand = new Command("install", "Install a new encryption key");
        var useCommand = new Command("use", "Change the primary encryption key");
        var removeCommand = new Command("remove", "Remove an encryption key");

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

        // List command
        listCommand.Add(rpcAddrOption);
        listCommand.Add(rpcAuthOption);
        listCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var addr = parseResult.GetValue(rpcAddrOption)!;
            var auth = parseResult.GetValue(rpcAuthOption);
            
            try
            {
                await using var client = await RpcHelper.ConnectAsync(addr, auth, cancellationToken);
                var response = await client.ListKeysAsync(cancellationToken);
                
                Console.WriteLine($"Keys in cluster: {response.Keys.Count}");
                Console.WriteLine($"Nodes: {response.NumNodes}, Responses: {response.NumResp}");
                Console.WriteLine();
                
                foreach (var kvp in response.Keys)
                {
                    Console.WriteLine($"  {kvp.Key} - {kvp.Value} node(s)");
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error: {ex.Message}");
                return 1;
            }
        });

        // Install command
        var keyArgument = new Argument<string>("key")
        {
            Description = "Base64-encoded encryption key"
        };
        installCommand.Add(keyArgument);
        installCommand.Add(rpcAddrOption);
        installCommand.Add(rpcAuthOption);
        installCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var key = parseResult.GetValue(keyArgument)!;
            var addr = parseResult.GetValue(rpcAddrOption)!;
            var auth = parseResult.GetValue(rpcAuthOption);
            
            try
            {
                await using var client = await RpcHelper.ConnectAsync(addr, auth, cancellationToken);
                var response = await client.InstallKeyAsync(key, cancellationToken);
                
                Console.WriteLine("Successfully installed key");
                Console.WriteLine($"Nodes: {response.NumNodes}, Responses: {response.NumResp}");

                if (response.Messages.Length <= 0) return 0;
                
                Console.WriteLine("Messages:");
                foreach (var msg in response.Messages)
                {
                    Console.WriteLine($"  {msg}");
                }

                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error: {ex.Message}");
                return 1;
            }
        });

        // Use command
        var useKeyArgument = new Argument<string>("key")
        {
            Description = "Base64-encoded encryption key to use as primary"
        };
        useCommand.Add(useKeyArgument);
        useCommand.Add(rpcAddrOption);
        useCommand.Add(rpcAuthOption);
        useCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var key = parseResult.GetValue(useKeyArgument)!;
            var addr = parseResult.GetValue(rpcAddrOption)!;
            var auth = parseResult.GetValue(rpcAuthOption);
            
            try
            {
                await using var client = await RpcHelper.ConnectAsync(addr, auth, cancellationToken);
                var response = await client.UseKeyAsync(key, cancellationToken);
                
                Console.WriteLine("Successfully changed primary key");
                Console.WriteLine($"Nodes: {response.NumNodes}, Responses: {response.NumResp}");
                
                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error: {ex.Message}");
                return 1;
            }
        });

        // Remove command
        var removeKeyArgument = new Argument<string>("key")
        {
            Description = "Base64-encoded encryption key to remove"
        };
        removeCommand.Add(removeKeyArgument);
        removeCommand.Add(rpcAddrOption);
        removeCommand.Add(rpcAuthOption);
        removeCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var key = parseResult.GetValue(removeKeyArgument)!;
            var addr = parseResult.GetValue(rpcAddrOption)!;
            var auth = parseResult.GetValue(rpcAuthOption);
            
            try
            {
                await using var client = await RpcHelper.ConnectAsync(addr, auth, cancellationToken);
                var response = await client.RemoveKeyAsync(key, cancellationToken);
                
                Console.WriteLine("Successfully removed key");
                Console.WriteLine($"Nodes: {response.NumNodes}, Responses: {response.NumResp}");
                
                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error removing key: {ex.Message}");
                return 1;
            }
        });

        command.Add(listCommand);
        command.Add(installCommand);
        command.Add(useCommand);
        command.Add(removeCommand);

        return command;
    }
}
