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

        var configFileOption = new Option<string?>("--config-file")
        {
            Description = "Path to a config file or directory of .json files (loaded before CLI overrides)"
        };

        var eventHandlerOption = new Option<string[]?>("--event-handler")
        {
            Description = "Event handler specification (can be repeated)",
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
        command.Add(configFileOption);
        command.Add(eventHandlerOption);

        command.SetAction(async (parseResult, _) =>
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
            var configPath = parseResult.GetValue(configFileOption);
            var handlerSpecs = parseResult.GetValue(eventHandlerOption);

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
                    configPath,
                    handlerSpecs,
                    shutdownToken);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error: {ex.Message}");
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
        string? configPath,
        string[]? handlerSpecs,
        CancellationToken shutdownToken)
    {
        // Build agent configuration from CLI
        var cliConfig = new AgentConfig
        {
            NodeName = nodeName ?? Environment.MachineName,
            BindAddr = bindAddr,
            AdvertiseAddr = advertiseAddr,
            EncryptKey = encryptKey,
            Tags = ParseTags(tags),
            RpcAddr = rpcAddr,
            RpcAuthKey = rpcAuth,
            EventHandlers = handlerSpecs?.ToList() ?? []
        };

        // Load the config file or directory if provided, then merge with CLI where CLI overrides a file
        var finalConfig = cliConfig;
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            AgentConfig loaded;
            if (Directory.Exists(configPath))
            {
                loaded = await ConfigLoader.LoadFromDirectoryAsync(configPath, shutdownToken);
            }
            else if (File.Exists(configPath))
            {
                loaded = await ConfigLoader.LoadFromFileAsync(configPath, shutdownToken);
            }
            else
            {
                throw new FileNotFoundException($"Config file or directory not found: {configPath}");
            }

            // File provides base, CLI overrides
            finalConfig = AgentConfig.Merge(loaded, cliConfig);
        }

        // Create and start an agent
        var agent = new SerfAgent(finalConfig);

        try
        {
            // Set up signal handling for SIGHUP-based reload of script handlers
            using var signalHandler = new SignalHandler();
            signalHandler.RegisterCallback(signal =>
            {
                if (signal != Signal.SIGHUP)
                    return;

                // Fire-and-forget to avoid blocking a signal thread
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var reloaded = cliConfig; // base
                        if (!string.IsNullOrWhiteSpace(configPath))
                        {
                            AgentConfig loaded;
                            if (Directory.Exists(configPath))
                                loaded = await ConfigLoader.LoadFromDirectoryAsync(configPath, shutdownToken);
                            else if (File.Exists(configPath))
                                loaded = await ConfigLoader.LoadFromFileAsync(configPath, shutdownToken);
                            else
                                throw new FileNotFoundException($"Config file or directory not found: {configPath}");

                            reloaded = AgentConfig.Merge(loaded, cliConfig);
                        }

                        agent.UpdateEventHandlers(reloaded.EventHandlers);
                        Console.WriteLine($"Reloaded {reloaded.EventHandlers.Count} event handler(s)");
                    }
                    catch (Exception ex)
                    {
                        await Console.Error.WriteLineAsync($"Reload failed: {ex.Message}");
                    }
                }, shutdownToken);
            });

            await agent.StartAsync(shutdownToken);

            // Join cluster if specified
            if (!string.IsNullOrEmpty(joinAddr))
            {
                try
                {
                    var joinResult = await agent.Serf!.JoinAsync([joinAddr], replay);
                    if (joinResult == 0)
                    {
                        await Console.Error.WriteLineAsync($"Failed to join any nodes at {joinAddr}");
                        return 1;
                    }
                    Console.WriteLine($"Successfully joined {joinResult} node(s)");
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"Failed to join any nodes ({ex.Message})");
                    return 1;
                }
            }

            Console.WriteLine($"Serf agent running on {bindAddr}");
            Console.WriteLine($"RPC endpoint: {rpcAddr}");
            Console.WriteLine("Press Ctrl+C to shutdown");

            // Wait for a shutdown signal
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
            await Console.Error.WriteLineAsync($"Agent error: {ex.Message}");
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
