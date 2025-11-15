// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Helpers;
using NSerf.Client;
using NSerf.Lighthouse.Client;
using NSerf.Lighthouse.Client.Models;

namespace NSerf.CLI.Tests.Commands;

/// <summary>
/// Tests for Lighthouse-related flags on the agent command.
/// These focus on CLI-level validation and wiring without hitting the real Lighthouse service.
/// </summary>
[Trait("Category", "CLI-Lighthouse")]
[Collection("Sequential")] // Reuse sequential collection to avoid port conflicts
public class AgentLighthouseCommandTests
{
    [Fact(Timeout = 5000)]
    public async Task AgentCommand_LighthouseStartJoinEnabledWithoutSecrets_ExitsWithError()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var bindAddr = TestHelper.GetRandomBindAddr();
        var rpcAddr = "127.0.0.1:0";

        var rootCommand = new RootCommand { AgentCommand.Create(cts.Token) };

        var args = new[]
        {
            "agent",
            "--bind", bindAddr,
            "--rpc-addr", rpcAddr,
            "--lighthouse-start-join",
            "--lighthouse-version-name", "cli-test",
            "--lighthouse-version-number", "1"
        };

        // Act
        var (exitCode, _, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert - should fail with a helpful Lighthouse error
        Assert.NotEqual(0, exitCode);
        Assert.Contains("Lighthouse join is enabled", error);
    }

    [Fact(Timeout = 5000)]
    public async Task AgentCommand_LighthouseRetryJoinEnabledWithoutSecrets_ExitsWithError()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var bindAddr = TestHelper.GetRandomBindAddr();
        const string rpcAddr = "127.0.0.1:0";

        var rootCommand = new RootCommand { AgentCommand.Create(cts.Token) };

        var args = new[]
        {
            "agent",
            "--bind", bindAddr,
            "--rpc-addr", rpcAddr,
            "--lighthouse-retry-join",
            "--lighthouse-version-name", "cli-test",
            "--lighthouse-version-number", "1"
        };

        // Act
        var (exitCode, _, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert - should fail with a helpful Lighthouse error
        Assert.NotEqual(0, exitCode);
        Assert.Contains("Lighthouse join is enabled", error);
    }

    [Fact(Timeout = 30000)]
    public async Task AgentCommand_LighthouseStartJoin_UsesHostedLighthouseToFormCluster()
    {
        // Arrange - generate ephemeral keys and cluster ID
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const string versionName = "cli-lighthouse-start";
        const long versionNumber = 1;
        const int timeoutSeconds = 30;

        // Build Lighthouse client (test-side) and register cluster
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://api-lighthouse.nserf.org";
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = timeoutSeconds;
        });

        await using var provider = services.BuildServiceProvider();
        var lighthouseClient = provider.GetRequiredService<ILighthouseClient>();

        var registered = await lighthouseClient.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);
        Assert.True(registered);

        // Start a seed agent (not using Lighthouse itself)
        var seedConfig = new NSerf.Agent.AgentConfig
        {
            NodeName = "cli-lh-seed-start",
            BindAddr = "127.0.0.1:19341"
        };
        var seedAgent = new NSerf.Agent.SerfAgent(seedConfig);
        await seedAgent.StartAsync();

        // Register seed node with Lighthouse using the test client
        var seedNodeInfo = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19341,
            Metadata = new Dictionary<string, string> { ["node"] = "cli-seed-start" }
        };

        var peersFromSeed = await lighthouseClient.DiscoverNodesAsync(
            seedNodeInfo,
            versionName,
            versionNumber,
            CancellationToken.None);

        Assert.NotNull(peersFromSeed); // Seed is now registered

        // Prepare CLI root command and Lighthouse-enabled joiner
        using var cts = new CancellationTokenSource();
        var rootCommand = new RootCommand { AgentCommand.Create(cts.Token) };

        var joinerBindAddr = "127.0.0.1:19342";
        var rpcAddr = "127.0.0.1:19343";

        var args = new[]
        {
            "agent",
            "--node", "cli-lh-joiner-start",
            "--bind", joinerBindAddr,
            "--rpc-addr", rpcAddr,
            "--lighthouse-start-join",
            "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
            "--lighthouse-version-name", versionName,
            "--lighthouse-version-number", versionNumber.ToString(),
            "--lighthouse-cluster-id", clusterId,
            "--lighthouse-private-key", privateKey,
            "--lighthouse-aes-key", aesKey
        };

        // Act - start CLI agent in the background
        var agentTask = Task.Run(() => rootCommand.Parse(args).InvokeAsync(), cts.Token);

        try
        {
            // Wait for cluster to form from seed's perspective (up to 10 seconds)
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            var joined = false;
            while (DateTime.UtcNow < deadline)
            {
                var members = seedAgent.Serf?.Members() ?? [];
                if (members.Length == 2)
                {
                    joined = true;
                    break;
                }

                await Task.Delay(100, cts.Token);
            }

            Assert.True(joined, "Expected 2-node cluster via Lighthouse start join");

            // Also verify from the joiner's RPC endpoint
            await using var rpcClient = new RpcClient(new RpcConfig { Address = rpcAddr });
            await rpcClient.ConnectAsync();
            var membersFromJoiner = await rpcClient.MembersAsync();

            Assert.Equal(2, membersFromJoiner.Length);
            Assert.Contains(membersFromJoiner, m => m.Name == "cli-lh-seed-start");
            Assert.Contains(membersFromJoiner, m => m.Name == "cli-lh-joiner-start");
        }
        finally
        {
            // Cleanup CLI agent
            await cts.CancelAsync();
            await Task.WhenAny(agentTask, Task.Delay(5000, cts.Token));

            // Cleanup seed agent
            await seedAgent.ShutdownAsync();
            await seedAgent.DisposeAsync();
        }
    }

    [Fact(Timeout = 60000)]
    public async Task AgentCommand_LighthouseStartJoin_MultipleCliAgents_FormThreeNodeCluster()
    {
        // Arrange - generate ephemeral keys and cluster ID
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const string versionName = "cli-lighthouse-multi";
        const long versionNumber = 1;

        // Build Lighthouse client (test-side) and register cluster
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://api-lighthouse.nserf.org";
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var lighthouseClient = provider.GetRequiredService<ILighthouseClient>();

        var registered = await lighthouseClient.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);
        Assert.True(registered);

        // Start seed agent
        var seedConfig = new NSerf.Agent.AgentConfig
        {
            NodeName = "cli-lh-seed-multi",
            BindAddr = "127.0.0.1:19351"
        };
        var seedAgent = new NSerf.Agent.SerfAgent(seedConfig);
        await seedAgent.StartAsync();

        var seedNodeInfo = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19351,
            Metadata = new Dictionary<string, string> { ["node"] = "cli-seed-multi" }
        };

        await lighthouseClient.DiscoverNodesAsync(
            seedNodeInfo,
            versionName,
            versionNumber,
            CancellationToken.None);

        // First CLI agent
        using var cts1 = new CancellationTokenSource();
        var rootCommand1 = new RootCommand { AgentCommand.Create(cts1.Token) };

        var joiner1BindAddr = "127.0.0.1:19352";
        var rpcAddr1 = "127.0.0.1:19353";

        var args1 = new[]
        {
            "agent",
            "--node", "cli-lh-joiner1",
            "--bind", joiner1BindAddr,
            "--rpc-addr", rpcAddr1,
            "--lighthouse-start-join",
            "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
            "--lighthouse-version-name", versionName,
            "--lighthouse-version-number", versionNumber.ToString(),
            "--lighthouse-cluster-id", clusterId,
            "--lighthouse-private-key", privateKey,
            "--lighthouse-aes-key", aesKey
        };

        var agentTask1 = Task.Run(() => rootCommand1.Parse(args1).InvokeAsync());

        // Wait until seed sees 2 nodes
        var deadline1 = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var cluster2Formed = false;
        while (DateTime.UtcNow < deadline1)
        {
            var members = seedAgent.Serf?.Members() ?? [];
            if (members.Length == 2)
            {
                cluster2Formed = true;
                break;
            }
            await Task.Delay(200);
        }

        Assert.True(cluster2Formed, "Expected 2-node cluster (seed + first CLI agent)");

        // Register first CLI agent in Lighthouse so second CLI agent can see both
        var joiner1NodeInfo = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19352,
            Metadata = new Dictionary<string, string> { ["node"] = "cli-joiner1" }
        };

        await lighthouseClient.DiscoverNodesAsync(
            joiner1NodeInfo,
            versionName,
            versionNumber,
            CancellationToken.None);

        // Second CLI agent
        using var cts2 = new CancellationTokenSource();
        var rootCommand2 = new RootCommand { AgentCommand.Create(cts2.Token) };

        var joiner2BindAddr = "127.0.0.1:19354";
        var rpcAddr2 = "127.0.0.1:19355";

        var args2 = new[]
        {
            "agent",
            "--node", "cli-lh-joiner2",
            "--bind", joiner2BindAddr,
            "--rpc-addr", rpcAddr2,
            "--lighthouse-start-join",
            "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
            "--lighthouse-version-name", versionName,
            "--lighthouse-version-number", versionNumber.ToString(),
            "--lighthouse-cluster-id", clusterId,
            "--lighthouse-private-key", privateKey,
            "--lighthouse-aes-key", aesKey
        };

        var agentTask2 = Task.Run(() => rootCommand2.Parse(args2).InvokeAsync());

        try
        {
            // Wait until seed sees 3 nodes
            var deadline2 = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            var cluster3Formed = false;
            while (DateTime.UtcNow < deadline2)
            {
                var members = seedAgent.Serf?.Members() ?? [];
                if (members.Length == 3)
                {
                    cluster3Formed = true;
                    break;
                }
                await Task.Delay(200);
            }

            Assert.True(cluster3Formed, "Expected 3-node cluster (seed + two CLI agents)");

            // Verify from both CLI agents via RPC
            await using var rpcClient1 = new RpcClient(new RpcConfig { Address = rpcAddr1 });
            await rpcClient1.ConnectAsync();
            var membersFromCli1 = await rpcClient1.MembersAsync();
            Assert.Equal(3, membersFromCli1.Length);

            await using var rpcClient2 = new RpcClient(new RpcConfig { Address = rpcAddr2 });
            await rpcClient2.ConnectAsync();
            var membersFromCli2 = await rpcClient2.MembersAsync();
            Assert.Equal(3, membersFromCli2.Length);
        }
        finally
        {
            // Cleanup CLI agents
            cts2.Cancel();
            await Task.WhenAny(agentTask2, Task.Delay(5000));

            cts1.Cancel();
            await Task.WhenAny(agentTask1, Task.Delay(5000));

            // Cleanup seed agent
            await seedAgent.ShutdownAsync();
            await seedAgent.DisposeAsync();
        }
    }

    [Fact(Timeout = 40000)]
    public async Task AgentCommand_LighthouseVersionIsolation_DifferentVersionsDontSeeEachOther()
    {
        // Test that nodes with different version names are isolated from each other
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");

        // Build Lighthouse client
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://api-lighthouse.nserf.org";
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var lighthouseClient = provider.GetRequiredService<ILighthouseClient>();
        await lighthouseClient.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);

        // Start v1 seed
        var v1SeedConfig = new NSerf.Agent.AgentConfig
        {
            NodeName = "cli-v1-seed",
            BindAddr = "127.0.0.1:19361"
        };
        var v1SeedAgent = new NSerf.Agent.SerfAgent(v1SeedConfig);
        await v1SeedAgent.StartAsync();

        await lighthouseClient.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "127.0.0.1", Port = 19361, Metadata = new Dictionary<string, string>() },
            "version-v1",
            1,
            CancellationToken.None);

        // Start v2 seed
        var v2SeedConfig = new NSerf.Agent.AgentConfig
        {
            NodeName = "cli-v2-seed",
            BindAddr = "127.0.0.1:19362"
        };
        var v2SeedAgent = new NSerf.Agent.SerfAgent(v2SeedConfig);
        await v2SeedAgent.StartAsync();

        await lighthouseClient.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "127.0.0.1", Port = 19362, Metadata = new Dictionary<string, string>() },
            "version-v2",
            1,
            CancellationToken.None);

        // CLI agent joins v1
        using var ctsV1 = new CancellationTokenSource();
        var rootCommandV1 = new RootCommand { AgentCommand.Create(ctsV1.Token) };

        var v1JoinerBindAddr = "127.0.0.1:19363";
        var v1RpcAddr = "127.0.0.1:19364";

        var argsV1 = new[]
        {
            "agent",
            "--node", "cli-v1-joiner",
            "--bind", v1JoinerBindAddr,
            "--rpc-addr", v1RpcAddr,
            "--lighthouse-start-join",
            "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
            "--lighthouse-version-name", "version-v1",
            "--lighthouse-version-number", "1",
            "--lighthouse-cluster-id", clusterId,
            "--lighthouse-private-key", privateKey,
            "--lighthouse-aes-key", aesKey
        };

        var agentTaskV1 = Task.Run(() => rootCommandV1.Parse(argsV1).InvokeAsync());

        // CLI agent joins v2
        using var ctsV2 = new CancellationTokenSource();
        var rootCommandV2 = new RootCommand { AgentCommand.Create(ctsV2.Token) };

        var v2JoinerBindAddr = "127.0.0.1:19365";
        var v2RpcAddr = "127.0.0.1:19366";

        var argsV2 = new[]
        {
            "agent",
            "--node", "cli-v2-joiner",
            "--bind", v2JoinerBindAddr,
            "--rpc-addr", v2RpcAddr,
            "--lighthouse-start-join",
            "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
            "--lighthouse-version-name", "version-v2",
            "--lighthouse-version-number", "1",
            "--lighthouse-cluster-id", clusterId,
            "--lighthouse-private-key", privateKey,
            "--lighthouse-aes-key", aesKey
        };

        var agentTaskV2 = Task.Run(() => rootCommandV2.Parse(argsV2).InvokeAsync());

        try
        {
            // Wait for v1 cluster to form (2 nodes)
            var deadlineV1 = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            var v1ClusterFormed = false;
            while (DateTime.UtcNow < deadlineV1)
            {
                var members = v1SeedAgent.Serf?.Members() ?? [];
                if (members.Length == 2)
                {
                    v1ClusterFormed = true;
                    break;
                }
                await Task.Delay(200);
            }

            Assert.True(v1ClusterFormed, "Expected v1 cluster to have 2 nodes");

            // Wait for v2 cluster to form (2 nodes)
            var deadlineV2 = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            var v2ClusterFormed = false;
            while (DateTime.UtcNow < deadlineV2)
            {
                var members = v2SeedAgent.Serf?.Members() ?? [];
                if (members.Length == 2)
                {
                    v2ClusterFormed = true;
                    break;
                }
                await Task.Delay(200);
            }

            Assert.True(v2ClusterFormed, "Expected v2 cluster to have 2 nodes");

            // Verify v1 cluster only sees v1 nodes
            var v1Members = v1SeedAgent.Serf?.Members() ?? [];
            Assert.Equal(2, v1Members.Length);
            Assert.Contains(v1Members, m => m.Name == "cli-v1-seed");
            Assert.Contains(v1Members, m => m.Name == "cli-v1-joiner");

            // Verify v2 cluster only sees v2 nodes
            var v2Members = v2SeedAgent.Serf?.Members() ?? [];
            Assert.Equal(2, v2Members.Length);
            Assert.Contains(v2Members, m => m.Name == "cli-v2-seed");
            Assert.Contains(v2Members, m => m.Name == "cli-v2-joiner");
        }
        finally
        {
            ctsV2.Cancel();
            await Task.WhenAny(agentTaskV2, Task.Delay(5000));

            ctsV1.Cancel();
            await Task.WhenAny(agentTaskV1, Task.Delay(5000));

            await v2SeedAgent.ShutdownAsync();
            await v2SeedAgent.DisposeAsync();

            await v1SeedAgent.ShutdownAsync();
            await v1SeedAgent.DisposeAsync();
        }
    }

    [Fact(Timeout = 40000)]
    public async Task AgentCommand_LighthouseNodeFailure_RemainingNodesStayConnected()
    {
        // Test that when a node fails, remaining nodes stay connected via Lighthouse
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const string versionName = "cli-failure-test";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://api-lighthouse.nserf.org";
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var lighthouseClient = provider.GetRequiredService<ILighthouseClient>();
        await lighthouseClient.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);

        // Start 3 CLI agents
        using var cts1 = new CancellationTokenSource();
        var rootCommand1 = new RootCommand { AgentCommand.Create(cts1.Token) };

        var args1 = new[]
        {
            "agent",
            "--node", "cli-node1",
            "--bind", "127.0.0.1:19371",
            "--rpc-addr", "127.0.0.1:19372",
            "--lighthouse-start-join",
            "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
            "--lighthouse-version-name", versionName,
            "--lighthouse-version-number", "1",
            "--lighthouse-cluster-id", clusterId,
            "--lighthouse-private-key", privateKey,
            "--lighthouse-aes-key", aesKey
        };

        var agentTask1 = Task.Run(() => rootCommand1.Parse(args1).InvokeAsync());

        // Register node1 with Lighthouse
        await Task.Delay(1000);
        await lighthouseClient.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "127.0.0.1", Port = 19371, Metadata = new Dictionary<string, string>() },
            versionName,
            1,
            CancellationToken.None);

        using var cts2 = new CancellationTokenSource();
        var rootCommand2 = new RootCommand { AgentCommand.Create(cts2.Token) };

        var args2 = new[]
        {
            "agent",
            "--node", "cli-node2",
            "--bind", "127.0.0.1:19373",
            "--rpc-addr", "127.0.0.1:19374",
            "--lighthouse-start-join",
            "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
            "--lighthouse-version-name", versionName,
            "--lighthouse-version-number", "1",
            "--lighthouse-cluster-id", clusterId,
            "--lighthouse-private-key", privateKey,
            "--lighthouse-aes-key", aesKey
        };

        var agentTask2 = Task.Run(() => rootCommand2.Parse(args2).InvokeAsync());

        await Task.Delay(1000);
        await lighthouseClient.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "127.0.0.1", Port = 19373, Metadata = new Dictionary<string, string>() },
            versionName,
            1,
            CancellationToken.None);

        using var cts3 = new CancellationTokenSource();
        var rootCommand3 = new RootCommand { AgentCommand.Create(cts3.Token) };

        var args3 = new[]
        {
            "agent",
            "--node", "cli-node3",
            "--bind", "127.0.0.1:19375",
            "--rpc-addr", "127.0.0.1:19376",
            "--lighthouse-start-join",
            "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
            "--lighthouse-version-name", versionName,
            "--lighthouse-version-number", "1",
            "--lighthouse-cluster-id", clusterId,
            "--lighthouse-private-key", privateKey,
            "--lighthouse-aes-key", aesKey
        };

        var agentTask3 = Task.Run(() => rootCommand3.Parse(args3).InvokeAsync());

        try
        {
            // Wait for 3-node cluster to form
            await Task.Delay(5000);

            // Verify 3 nodes via RPC on node1
            await using var rpcClient1 = new RpcClient(new RpcConfig { Address = "127.0.0.1:19372" });
            await rpcClient1.ConnectAsync();
            var membersBeforeFailure = await rpcClient1.MembersAsync();
            Assert.Equal(3, membersBeforeFailure.Length);

            // Kill node2 (simulate failure)
            cts2.Cancel();
            await Task.WhenAny(agentTask2, Task.Delay(3000));

            // Wait for failure detection
            await Task.Delay(3000);

            // Verify node1 and node3 still see each other (node2 should be marked as failed/left)
            var membersAfterFailure = await rpcClient1.MembersAsync();
            Assert.Contains(membersAfterFailure, m => m.Name == "cli-node1" && m.Status == "alive");
            Assert.Contains(membersAfterFailure, m => m.Name == "cli-node3" && m.Status == "alive");

            // Node2 should be marked as failed or left
            var node2Member = membersAfterFailure.FirstOrDefault(m => m.Name == "cli-node2");
            Assert.NotNull(node2Member);
            Assert.True(node2Member.Status == "failed" || node2Member.Status == "left");
        }
        finally
        {
            cts3.Cancel();
            await Task.WhenAny(agentTask3, Task.Delay(3000));

            cts1.Cancel();
            await Task.WhenAny(agentTask1, Task.Delay(3000));
        }
    }

    [Fact(Timeout = 35000)]
    public async Task AgentCommand_LighthouseWithStaticJoin_CombinesBothSources()
    {
        // Test that CLI agent can combine Lighthouse discovery with static --join
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const string versionName = "cli-combined-join";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://api-lighthouse.nserf.org";
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var lighthouseClient = provider.GetRequiredService<ILighthouseClient>();
        await lighthouseClient.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);

        // Seed node 1 (registered with Lighthouse)
        var seed1Config = new NSerf.Agent.AgentConfig
        {
            NodeName = "cli-combined-seed1",
            BindAddr = "127.0.0.1:19381"
        };
        var seed1Agent = new NSerf.Agent.SerfAgent(seed1Config);
        await seed1Agent.StartAsync();

        await lighthouseClient.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "127.0.0.1", Port = 19381, Metadata = new Dictionary<string, string>() },
            versionName,
            1,
            CancellationToken.None);

        // Seed node 2 (NOT registered with Lighthouse, only static)
        var seed2Config = new NSerf.Agent.AgentConfig
        {
            NodeName = "cli-combined-seed2",
            BindAddr = "127.0.0.1:19382"
        };
        var seed2Agent = new NSerf.Agent.SerfAgent(seed2Config);
        await seed2Agent.StartAsync();

        // CLI agent joins using BOTH Lighthouse and static --join
        using var cts = new CancellationTokenSource();
        var rootCommand = new RootCommand { AgentCommand.Create(cts.Token) };

        var joinerBindAddr = "127.0.0.1:19383";
        var rpcAddr = "127.0.0.1:19384";

        var args = new[]
        {
            "agent",
            "--node", "cli-combined-joiner",
            "--bind", joinerBindAddr,
            "--rpc-addr", rpcAddr,
            "--join", "127.0.0.1:19382", // Static join to seed2
            "--lighthouse-start-join",
            "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
            "--lighthouse-version-name", versionName,
            "--lighthouse-version-number", "1",
            "--lighthouse-cluster-id", clusterId,
            "--lighthouse-private-key", privateKey,
            "--lighthouse-aes-key", aesKey
        };

        var agentTask = Task.Run(() => rootCommand.Parse(args).InvokeAsync());

        try
        {
            // Wait for 3-node cluster to form
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            var clusterFormed = false;
            while (DateTime.UtcNow < deadline)
            {
                var seedMembers = seed1Agent.Serf?.Members() ?? [];
                if (seedMembers.Length == 3)
                {
                    clusterFormed = true;
                    break;
                }
                await Task.Delay(200);
            }

            Assert.True(clusterFormed, "Expected 3-node cluster from combined Lighthouse + static join");

            // Verify all 3 nodes see each other
            await using var rpcClient = new RpcClient(new RpcConfig { Address = rpcAddr });
            await rpcClient.ConnectAsync();
            var members = await rpcClient.MembersAsync();

            Assert.Equal(3, members.Length);
            Assert.Contains(members, m => m.Name == "cli-combined-seed1");
            Assert.Contains(members, m => m.Name == "cli-combined-seed2");
            Assert.Contains(members, m => m.Name == "cli-combined-joiner");
        }
        finally
        {
            cts.Cancel();
            await Task.WhenAny(agentTask, Task.Delay(5000));

            await seed2Agent.ShutdownAsync();
            await seed2Agent.DisposeAsync();

            await seed1Agent.ShutdownAsync();
            await seed1Agent.DisposeAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task AgentCommand_LighthouseEmptyCluster_StartsAlone()
    {
        // Test that CLI agent starts successfully when Lighthouse returns no peers (first node in cluster)
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const string versionName = "cli-empty-cluster";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://api-lighthouse.nserf.org";
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var lighthouseClient = provider.GetRequiredService<ILighthouseClient>();
        await lighthouseClient.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);

        // Start CLI agent as first node (no peers in Lighthouse yet)
        using var cts = new CancellationTokenSource();
        var rootCommand = new RootCommand { AgentCommand.Create(cts.Token) };

        var bindAddr = "127.0.0.1:19391";
        var rpcAddr = "127.0.0.1:19392";

        var args = new[]
        {
            "agent",
            "--node", "cli-first-node",
            "--bind", bindAddr,
            "--rpc-addr", rpcAddr,
            "--lighthouse-start-join",
            "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
            "--lighthouse-version-name", versionName,
            "--lighthouse-version-number", "1",
            "--lighthouse-cluster-id", clusterId,
            "--lighthouse-private-key", privateKey,
            "--lighthouse-aes-key", aesKey
        };

        var agentTask = Task.Run(() => rootCommand.Parse(args).InvokeAsync());

        try
        {
            // Wait for agent to start
            await Task.Delay(2000);

            // Verify agent started successfully with only itself
            await using var rpcClient = new RpcClient(new RpcConfig { Address = rpcAddr });
            await rpcClient.ConnectAsync();
            var members = await rpcClient.MembersAsync();

            Assert.Single(members);
            Assert.Equal("cli-first-node", members[0].Name);
            Assert.Equal("alive", members[0].Status);
        }
        finally
        {
            cts.Cancel();
            await Task.WhenAny(agentTask, Task.Delay(5000));
        }
    }

    [Fact(Timeout = 35000)]
    public async Task AgentCommand_LighthouseConfigFromJsonFile_LoadsAndJoinsCluster()
    {
        // Test that CLI agent can load Lighthouse configuration from a JSON config file
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const string versionName = "cli-config-file";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://api-lighthouse.nserf.org";
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var lighthouseClient = provider.GetRequiredService<ILighthouseClient>();
        await lighthouseClient.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);

        // Start seed agent
        var seedConfig = new NSerf.Agent.AgentConfig
        {
            NodeName = "cli-config-seed",
            BindAddr = "127.0.0.1:19401"
        };
        var seedAgent = new NSerf.Agent.SerfAgent(seedConfig);
        await seedAgent.StartAsync();

        await lighthouseClient.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "127.0.0.1", Port = 19401, Metadata = new Dictionary<string, string>() },
            versionName,
            1,
            CancellationToken.None);

        // Create a temporary config file with Lighthouse settings
        var tempConfigDir = Path.Combine(Path.GetTempPath(), $"nserf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempConfigDir);
        var configFilePath = Path.Combine(tempConfigDir, "agent-config.json");

        var configJson = $$"""
        {
            "node_name": "cli-config-joiner",
            "bind_addr": "127.0.0.1:19402",
            "RPCAddr": "127.0.0.1:19403",
            "use_lighthouse_start_join": true,
            "lighthouse_version_name": "{{versionName}}",
            "lighthouse_version_number": 1
        }
        """;

        await File.WriteAllTextAsync(configFilePath, configJson);

        try
        {
            // CLI agent loads config from file and uses CLI flags for secrets
            using var cts = new CancellationTokenSource();
            var rootCommand = new RootCommand { AgentCommand.Create(cts.Token) };

            var args = new[]
            {
                "agent",
                "--config-file", configFilePath,
                "--lighthouse-base-url", "https://api-lighthouse.nserf.org",
                "--lighthouse-cluster-id", clusterId,
                "--lighthouse-private-key", privateKey,
                "--lighthouse-aes-key", aesKey
            };

            var agentTask = Task.Run(() => rootCommand.Parse(args).InvokeAsync());

            try
            {
                // Wait for CLI agent to start
                await Task.Delay(2000);

                // Wait for cluster to form
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                var clusterFormed = false;
                while (DateTime.UtcNow < deadline)
                {
                    var seedMembers = seedAgent.Serf?.Members() ?? [];
                    if (seedMembers.Length == 2)
                    {
                        clusterFormed = true;
                        break;
                    }
                    await Task.Delay(200);
                }

                Assert.True(clusterFormed, "Expected 2-node cluster with config loaded from JSON file");

                // Verify via RPC (use RPC address from config file)
                await using var rpcClient = new RpcClient(new RpcConfig { Address = "127.0.0.1:19403" });
                await rpcClient.ConnectAsync();
                var members = await rpcClient.MembersAsync();

                // Verify 2-node cluster formed (config file was loaded and Lighthouse worked)
                Assert.Equal(2, members.Length);
                Assert.Contains(members, m => m.Name == "cli-config-seed");
                Assert.Contains(members, m => m.Name == "cli-config-joiner");
            }
            finally
            {
                cts.Cancel();
                await Task.WhenAny(agentTask, Task.Delay(5000));

                await seedAgent.ShutdownAsync();
                await seedAgent.DisposeAsync();
            }
        }
        finally
        {
            // Cleanup temp config directory
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }
        }
    }

    [Fact(Timeout = 45000)]
    public async Task AgentCommand_ThreeAgentsWithFullLighthouseConfigFiles_FormCluster()
    {
        // Test that 3 CLI agents can form a cluster using ONLY config files (no CLI flags)
        // All Lighthouse settings including secrets are in the config files
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const string versionName = "cli-full-config";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://api-lighthouse.nserf.org";
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var lighthouseClient = provider.GetRequiredService<ILighthouseClient>();
        await lighthouseClient.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);

        // Create temp config directory
        var tempConfigDir = Path.Combine(Path.GetTempPath(), $"nserf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempConfigDir);

        try
        {
            // Create 3 config files with full Lighthouse configuration
            var configFiles = new[]
            {
                (Path: Path.Combine(tempConfigDir, "agent1-config.json"), NodeName: "cli-full-node1", BindAddr: "127.0.0.1:19411", RpcAddr: "127.0.0.1:19412"),
                (Path: Path.Combine(tempConfigDir, "agent2-config.json"), NodeName: "cli-full-node2", BindAddr: "127.0.0.1:19413", RpcAddr: "127.0.0.1:19414"),
                (Path: Path.Combine(tempConfigDir, "agent3-config.json"), NodeName: "cli-full-node3", BindAddr: "127.0.0.1:19415", RpcAddr: "127.0.0.1:19416")
            };

            foreach (var config in configFiles)
            {
                // Note: ConfigLoader uses JsonNamingPolicy.SnakeCaseLower which auto-converts property names
                // So LighthouseClusterId -> lighthouse_cluster_id automatically
                var configJson = $$"""
                {
                    "node_name": "{{config.NodeName}}",
                    "bind_addr": "{{config.BindAddr}}",
                    "RPCAddr": "{{config.RpcAddr}}",
                    "use_lighthouse_start_join": true,
                    "use_lighthouse_retry_join": true,
                    "lighthouse_version_name": "{{versionName}}",
                    "lighthouse_version_number": 1,
                    "lighthouse_base_url": "https://api-lighthouse.nserf.org",
                    "lighthouse_cluster_id": "{{clusterId}}",
                    "lighthouse_private_key": "{{privateKey}}",
                    "lighthouse_aes_key": "{{aesKey}}",
                    "lighthouse_timeout_seconds": 30
                }
                """;

                await File.WriteAllTextAsync(config.Path, configJson);
            }

            // Ensure files are written and flushed
            await Task.Delay(500);

            // Start all 3 CLI agents using only config files
            var ctsList = new[] { new CancellationTokenSource(), new CancellationTokenSource(), new CancellationTokenSource() };
            var agentTasks = new List<Task>();

            for (int i = 0; i < 3; i++)
            {
                var idx = i; // Capture for closure
                var rootCommand = new RootCommand { AgentCommand.Create(ctsList[idx].Token) };

                var args = new[]
                {
                    "agent",
                    "--config-file", configFiles[idx].Path
                };

                agentTasks.Add(Task.Run(() => rootCommand.Parse(args).InvokeAsync()));
            }

            try
            {
                // Wait for agents to start
                await Task.Delay(3000);

                // Wait for 3-node cluster to form
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
                var clusterFormed = false;
                RpcClient? rpcClient;

                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        rpcClient = new RpcClient(new RpcConfig { Address = configFiles[0].RpcAddr });
                        await rpcClient.ConnectAsync();
                        var members = await rpcClient.MembersAsync();

                        if (members.Length == 3)
                        {
                            clusterFormed = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Agent may not be ready yet
                    }

                    await Task.Delay(500);
                }

                Assert.True(clusterFormed, "Expected 3-node cluster with all config from files");

                // Verify all 3 nodes see each other
                await using (rpcClient = new RpcClient(new RpcConfig { Address = configFiles[0].RpcAddr }))
                {
                    await rpcClient.ConnectAsync();
                    var members = await rpcClient.MembersAsync();

                    Assert.Equal(3, members.Length);
                    Assert.Contains(members, m => m.Name == "cli-full-node1");
                    Assert.Contains(members, m => m.Name == "cli-full-node2");
                    Assert.Contains(members, m => m.Name == "cli-full-node3");
                }

                // Verify from another node's perspective
                await using (rpcClient = new RpcClient(new RpcConfig { Address = configFiles[1].RpcAddr }))
                {
                    await rpcClient.ConnectAsync();
                    var members = await rpcClient.MembersAsync();

                    Assert.Equal(3, members.Length);
                    Assert.Contains(members, m => m.Name == "cli-full-node1");
                    Assert.Contains(members, m => m.Name == "cli-full-node2");
                    Assert.Contains(members, m => m.Name == "cli-full-node3");
                }
            }
            finally
            {
                // Shutdown all agents
                foreach (var cts in ctsList)
                {
                    cts.Cancel();
                }

                await Task.WhenAll(agentTasks.Select(t => Task.WhenAny(t, Task.Delay(5000))));
            }
        }
        finally
        {
            // Cleanup temp config directory
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }
        }
    }
}
