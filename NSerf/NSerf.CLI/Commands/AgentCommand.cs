// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using NSerf.Agent;
using NSerf.Lighthouse.Client;

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

        var lighthouseBaseUrlOption = new Option<string?>("--lighthouse-base-url")
        {
            Description = "Lighthouse base URL"
        };

        var lighthouseClusterIdOption = new Option<string?>("--lighthouse-cluster-id")
        {
            Description = "Lighthouse cluster id (required when Lighthouse join is enabled)"
        };

        var lighthousePrivateKeyOption = new Option<string?>("--lighthouse-private-key")
        {
            Description = "Lighthouse private key (base64 ECDSA PKCS#8)"
        };

        var lighthouseAesKeyOption = new Option<string?>("--lighthouse-aes-key")
        {
            Description = "Lighthouse AES key (base64 32 bytes)"
        };

        var lighthouseTimeoutOption = new Option<int>("--lighthouse-timeout-seconds")
        {
            Description = "Lighthouse HTTP timeout in seconds",
            DefaultValueFactory = _ => 30
        };

        var lighthouseVersionNameOption = new Option<string?>("--lighthouse-version-name")
        {
            Description = "Lighthouse version name used to partition nodes (optional if set via config)"
        };

        var lighthouseVersionNumberOption = new Option<long?>("--lighthouse-version-number")
        {
            Description = "Lighthouse version number (> 0, optional if set via config)"
        };

        var lighthouseStartJoinOption = new Option<bool>("--lighthouse-start-join")
        {
            Description = "Use Lighthouse to discover peers on agent start join"
        };

        var lighthouseRetryJoinOption = new Option<bool>("--lighthouse-retry-join")
        {
            Description = "Use Lighthouse to discover peers for retry join"
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
        command.Add(lighthouseBaseUrlOption);
        command.Add(lighthouseClusterIdOption);
        command.Add(lighthousePrivateKeyOption);
        command.Add(lighthouseAesKeyOption);
        command.Add(lighthouseTimeoutOption);
        command.Add(lighthouseVersionNameOption);
        command.Add(lighthouseVersionNumberOption);
        command.Add(lighthouseStartJoinOption);
        command.Add(lighthouseRetryJoinOption);

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

            var lighthouseBaseUrl = parseResult.GetValue(lighthouseBaseUrlOption);
            var lighthouseClusterId = parseResult.GetValue(lighthouseClusterIdOption);
            var lighthousePrivateKey = parseResult.GetValue(lighthousePrivateKeyOption);
            var lighthouseAesKey = parseResult.GetValue(lighthouseAesKeyOption);
            var lighthouseTimeoutSeconds = parseResult.GetValue(lighthouseTimeoutOption);
            var lighthouseVersionName = parseResult.GetValue(lighthouseVersionNameOption);
            var lighthouseVersionNumber = parseResult.GetValue(lighthouseVersionNumberOption);
            var lighthouseStartJoin = parseResult.GetValue(lighthouseStartJoinOption);
            var lighthouseRetryJoin = parseResult.GetValue(lighthouseRetryJoinOption);

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
                    lighthouseBaseUrl,
                    lighthouseClusterId,
                    lighthousePrivateKey,
                    lighthouseAesKey,
                    lighthouseTimeoutSeconds,
                    lighthouseVersionName,
                    lighthouseVersionNumber,
                    lighthouseStartJoin,
                    lighthouseRetryJoin,
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
        string? lighthouseBaseUrl,
        string? lighthouseClusterId,
        string? lighthousePrivateKey,
        string? lighthouseAesKey,
        int lighthouseTimeoutSeconds,
        string? lighthouseVersionName,
        long? lighthouseVersionNumber,
        bool lighthouseStartJoin,
        bool lighthouseRetryJoin,
        CancellationToken shutdownToken)
    {
        // Build CLI config - only contains values explicitly set via CLI flags
        // This matches Go's behavior where cmdConfig only has non-zero values
        var cliConfig = new AgentConfig(replayOnJoin: replay)
        {
            // Set to empty so merge logic knows these weren't provided
            BindAddr = string.Empty,
            NodeName = string.Empty,
            LogLevel = string.Empty,
            Profile = string.Empty,
            SyslogFacility = string.Empty
        };

        // Only set values that were explicitly provided via CLI flags
        if (!string.IsNullOrWhiteSpace(nodeName))
            cliConfig.NodeName = nodeName;
        if (!string.IsNullOrWhiteSpace(bindAddr) && bindAddr != "0.0.0.0:7946")
            cliConfig.BindAddr = bindAddr;
        if (!string.IsNullOrWhiteSpace(advertiseAddr))
            cliConfig.AdvertiseAddr = advertiseAddr;
        if (!string.IsNullOrWhiteSpace(encryptKey))
            cliConfig.EncryptKey = encryptKey;
        if (!string.IsNullOrWhiteSpace(rpcAddr) && rpcAddr != "127.0.0.1:7373")
            cliConfig.RpcAddr = rpcAddr;
        if (!string.IsNullOrWhiteSpace(rpcAuth))
            cliConfig.RpcAuthKey = rpcAuth;
        if (tags != null && tags.Length > 0)
            cliConfig.Tags = ParseTags(tags);
        if (handlerSpecs != null && handlerSpecs.Length > 0)
            cliConfig.EventHandlers = [.. handlerSpecs];
        if (lighthouseStartJoin)
            cliConfig.UseLighthouseStartJoin = true;
        if (lighthouseRetryJoin)
            cliConfig.UseLighthouseRetryJoin = true;
        if (!string.IsNullOrWhiteSpace(lighthouseVersionName))
            cliConfig.LighthouseVersionName = lighthouseVersionName;
        if (lighthouseVersionNumber.HasValue && lighthouseVersionNumber.Value != 0)
            cliConfig.LighthouseVersionNumber = lighthouseVersionNumber.Value;

        // CRITICAL: Match Go's config loading order:
        // 1. Start with defaults
        // 2. Merge file config into defaults
        // 3. Merge CLI config into result
        var finalConfig = AgentConfig.Default();

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

            // Merge file config into defaults
            finalConfig = AgentConfig.Merge(finalConfig, loaded);
        }

        // Merge CLI config into result (CLI overrides file and defaults)
        finalConfig = AgentConfig.Merge(finalConfig, cliConfig);

        // Apply final defaults for required fields
        if (string.IsNullOrWhiteSpace(finalConfig.NodeName))
            finalConfig.NodeName = Environment.MachineName;

        // Optionally create a Lighthouse client if requested
        ILighthouseClient? lighthouseClient = null;
        if (finalConfig.UseLighthouseStartJoin || finalConfig.UseLighthouseRetryJoin)
        {
            // Check if secrets are provided via CLI flags OR config file
            var effectiveClusterId = lighthouseClusterId ?? finalConfig.LighthouseClusterId;
            var effectivePrivateKey = lighthousePrivateKey ?? finalConfig.LighthousePrivateKey;
            var effectiveAesKey = lighthouseAesKey ?? finalConfig.LighthouseAesKey;

            if (string.IsNullOrWhiteSpace(effectiveClusterId) ||
                string.IsNullOrWhiteSpace(effectivePrivateKey) ||
                string.IsNullOrWhiteSpace(effectiveAesKey))
            {
                await Console.Error.WriteLineAsync("Lighthouse join is enabled but lighthouse_cluster_id, lighthouse_private_key, or lighthouse_aes_key is missing (provide via CLI flags or config file).");
                return 1;
            }

            // At this point the effective values are guaranteed to be non-null/non-whitespace
            var nonNullClusterId = effectiveClusterId!;
            var nonNullPrivateKey = effectivePrivateKey!;
            var nonNullAesKey = effectiveAesKey!;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLighthouseClient(options =>
            {
                // Prefer CLI flags, fallback to config file values
                if (!string.IsNullOrWhiteSpace(lighthouseBaseUrl))
                {
                    options.BaseUrl = lighthouseBaseUrl;
                }
                else if (!string.IsNullOrWhiteSpace(finalConfig.LighthouseBaseUrl))
                {
                    options.BaseUrl = finalConfig.LighthouseBaseUrl;
                }

                options.ClusterId = nonNullClusterId;
                options.PrivateKey = nonNullPrivateKey;
                options.AesKey = nonNullAesKey;
                options.TimeoutSeconds = lighthouseTimeoutSeconds > 0 ? lighthouseTimeoutSeconds : finalConfig.LighthouseTimeoutSeconds;
            });

            var provider = services.BuildServiceProvider();
            lighthouseClient = provider.GetRequiredService<ILighthouseClient>();
        }

        // Create and start an agent
        var agent = new SerfAgent(finalConfig, logger: null, lighthouseClient: lighthouseClient);

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

            Console.WriteLine($"Serf agent running on {finalConfig.BindAddr}");
            Console.WriteLine($"RPC endpoint: {finalConfig.RpcAddr}");
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
