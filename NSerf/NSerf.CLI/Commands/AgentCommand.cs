// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.Agent;

namespace NSerf.CLI.Commands;

/// <summary>
/// Agent command - starts a long-running Serf agent.
/// This is the core command that all other commands depend on.
/// Port of: serf/cmd/serf/command/agent/command.go
/// </summary>
public static class AgentCommand
{
    public static Command Create(CancellationToken shutdownToken = default)
    {
        var command = new Command("agent", "Start the Serf agent");

        var nodeOption = new Option<string?>("--node")
        {
            Description = "Node name (default: hostname)"
        };

        var bindOption = new Option<string>("--bind")
        {
            Description = "Bind address (default: 0.0.0.0:7946)",
            DefaultValueFactory = _ => "0.0.0.0:7946"
        };

        var advertiseOption = new Option<string?>("--advertise")
        {
            Description = "Advertise address"
        };

        var rpcAddrOption = new Option<string>("--rpc-addr")
        {
            Description = "RPC bind address (default: 127.0.0.1:7373)",
            DefaultValueFactory = _ => "127.0.0.1:7373"
        };

        var rpcAuthOption = new Option<string?>("--rpc-auth")
        {
            Description = "RPC authentication token"
        };

        var encryptOption = new Option<string?>("--encrypt")
        {
            Description = "Encryption key (16-byte base64)"
        };

        var joinOption = new Option<string?>("--join")
        {
            Description = "Address to join at startup"
        };

        var replayOption = new Option<bool>("--replay")
        {
            Description = "Replay user events on join"
        };

        var tagOption = new Option<string[]?>("--tag")
        {
            Description = "Node tag (key=value)",
            AllowMultipleArgumentsPerToken = true
        };

        command.Add(nodeOption);
        command.Add(bindOption);
        command.Add(advertiseOption);
        command.Add(rpcAddrOption);
        command.Add(rpcAuthOption);
        command.Add(encryptOption);
        command.Add(joinOption);
        command.Add(replayOption);
        command.Add(tagOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var nodeName = parseResult.GetValue(nodeOption);
            var bindAddr = parseResult.GetValue(bindOption)!;
            var advertiseAddr = parseResult.GetValue(advertiseOption);
            var rpcAddr = parseResult.GetValue(rpcAddrOption)!;
            var rpcAuth = parseResult.GetValue(rpcAuthOption);
            var encryptKey = parseResult.GetValue(encryptOption);
            var joinAddr = parseResult.GetValue(joinOption);
            var replay = parseResult.GetValue(replayOption);
            var tags = parseResult.GetValue(tagOption);

            try
            {
                return await ExecuteAsync(
                    nodeName,
                    bindAddr,
                    advertiseAddr,
                    rpcAddr,
                    rpcAuth,
                    encryptKey,
                    joinAddr,
                    replay,
                    tags,
                    shutdownToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string? nodeName,
        string bindAddr,
        string? advertiseAddr,
        string rpcAddr,
        string? rpcAuth,
        string? encryptKey,
        string? joinAddr,
        bool replay,
        string[]? tags,
        CancellationToken shutdownToken)
    {
        // Build agent configuration
        var config = new AgentConfig
        {
            NodeName = nodeName ?? Environment.MachineName,
            BindAddr = bindAddr,
            AdvertiseAddr = advertiseAddr,
            EncryptKey = encryptKey,
            Tags = ParseTags(tags),
            RPCAddr = rpcAddr,
            RPCAuthKey = rpcAuth
        };

        // Create and start agent
        var agent = new SerfAgent(config);
        
        try
        {
            await agent.StartAsync(shutdownToken);

            // Join cluster if specified
            if (!string.IsNullOrEmpty(joinAddr))
            {
                try
                {
                    var joinResult = await agent.Serf!.JoinAsync(new[] { joinAddr }, replay);
                    if (joinResult == 0)
                    {
                        Console.Error.WriteLine($"Failed to join any nodes at {joinAddr}");
                        return 1;
                    }
                    Console.WriteLine($"Successfully joined {joinResult} node(s)");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to join any nodes ({ex.Message})");
                    return 1;
                }
            }

            Console.WriteLine($"Serf agent running on {bindAddr}");
            Console.WriteLine($"RPC endpoint: {rpcAddr}");
            Console.WriteLine("Press Ctrl+C to shutdown");

            // Wait for shutdown signal
            await Task.Delay(Timeout.Infinite, shutdownToken);
            
            return 0;
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
            Console.WriteLine("Shutting down...");
            await agent.ShutdownAsync();
            await agent.DisposeAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Agent error: {ex.Message}");
            await agent.ShutdownAsync();
            await agent.DisposeAsync();
            return 1;
        }
    }

    private static Dictionary<string, string> ParseTags(string[]? tags)
    {
        var result = new Dictionary<string, string>();
        
        if (tags == null)
            return result;

        foreach (var tag in tags)
        {
            var parts = tag.Split('=', 2);
            if (parts.Length == 2)
            {
                result[parts[0]] = parts[1];
            }
        }

        return result;
    }
}
