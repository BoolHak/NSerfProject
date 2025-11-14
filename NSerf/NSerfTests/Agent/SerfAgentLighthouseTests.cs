// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NSerf.Agent;
using NSerf.Lighthouse.Client;
using NSerf.Lighthouse.Client.Models;
using NSerf.Serf;

namespace NSerfTests.Agent;

/// <summary>
/// Integration-style tests for SerfAgent Lighthouse-based start and retry join.
/// Uses real Serf clusters with a mocked ILighthouseClient.
/// </summary>
[Collection("Sequential")] // Avoid port conflicts with other integration tests
public class SerfAgentLighthouseTests
{
    //this is a test server that I am hosting for testing purposes, you can use your own server 
    //all source code of the server is available at https://github.com/BoolHak/NSerf.Lighthouse
    private const string BaseUrl = "https://api-lighthouse.nserf.org";

    [Fact(Timeout = 15000)]
    public async Task StartJoin_UsesLighthousePeers_ToJoinCluster()
    {
        // Arrange - start a seed node
        var seedConfig = new AgentConfig
        {
            NodeName = "seed-node",
            BindAddr = "127.0.0.1:19001"
        };
        var seedAgent = new SerfAgent(seedConfig);
        await seedAgent.StartAsync();

        // Mock Lighthouse to return the seed node as a peer
        var lighthouseMock = new Mock<ILighthouseClient>();
        lighthouseMock
            .Setup(c => c.DiscoverNodesAsync(
                It.IsAny<NodeInfo>(),
                "test-version",
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NodeInfo
                {
                    IpAddress = "127.0.0.1",
                    Port = 19001,
                    Metadata = new Dictionary<string, string>()
                }
            ]);

        var joinConfig = new AgentConfig
        {
            NodeName = "joiner-node",
            BindAddr = "127.0.0.1:19002",
            UseLighthouseStartJoin = true,
            LighthouseVersionName = "test-version",
            LighthouseVersionNumber = 1
        };

        var joinerAgent = new SerfAgent(joinConfig, logger: null, lighthouseClient: lighthouseMock.Object);

        // Act - start the joiner, which should use Lighthouse for start join
        await joinerAgent.StartAsync();

        // Allow gossip to propagate
        await Task.Delay(500);

        // Assert - both nodes should see a 2-node cluster
        var membersSeed = seedAgent.Serf?.Members() ?? [];
        var membersJoiner = joinerAgent.Serf?.Members() ?? [];

        Assert.Equal(2, membersSeed.Length);
        Assert.Equal(2, membersJoiner.Length);

        // Both nodes should see each other by name and be alive
        Assert.Contains(membersSeed, m => m is { Name: "seed-node", Status: MemberStatus.Alive });
        Assert.Contains(membersSeed, m => m is { Name: "joiner-node", Status: MemberStatus.Alive });

        Assert.Contains(membersJoiner, m => m is { Name: "seed-node", Status: MemberStatus.Alive });
        Assert.Contains(membersJoiner, m => m is { Name: "joiner-node", Status: MemberStatus.Alive });

        lighthouseMock.Verify(c => c.DiscoverNodesAsync(
            It.IsAny<NodeInfo>(),
            "test-version",
            1,
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // Cleanup
        await joinerAgent.ShutdownAsync();
        await seedAgent.ShutdownAsync();
        await joinerAgent.DisposeAsync();
        await seedAgent.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task RetryJoin_UsesLighthousePeers_ToJoinCluster()
    {
        // Arrange - joiner node starts first and will retry join via Lighthouse
        var lighthouseMock = new Mock<ILighthouseClient>();

        const string versionName = "retry-test";
        const long versionNumber = 1;

        // Seed node endpoint (will be started later)
        const int seedPort = 19011;

        lighthouseMock
            .Setup(c => c.DiscoverNodesAsync(
                It.IsAny<NodeInfo>(),
                versionName,
                versionNumber,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NodeInfo
                {
                    IpAddress = "127.0.0.1",
                    Port = seedPort,
                    Metadata = new Dictionary<string, string>()
                }
            ]);

        var joinerConfig = new AgentConfig
        {
            NodeName = "retry-joiner",
            BindAddr = "127.0.0.1:19012",
            RetryJoin = [],
            RetryInterval = TimeSpan.FromMilliseconds(200),
            RetryMaxAttempts = 10,
            UseLighthouseRetryJoin = true,
            LighthouseVersionName = versionName,
            LighthouseVersionNumber = versionNumber
        };

        var joinerAgent = new SerfAgent(joinerConfig, logger: null, lighthouseClient: lighthouseMock.Object);
        await joinerAgent.StartAsync();

        // Start a seed node after a short delay so initial retries fail, later succeed
        var seedConfig = new AgentConfig
        {
            NodeName = "retry-seed",
            BindAddr = $"127.0.0.1:{seedPort}"
        };
        var seedAgent = new SerfAgent(seedConfig);
        await seedAgent.StartAsync();

        // Allow several retry intervals for the cluster to form
        await Task.Delay(2500);

        var membersSeed = seedAgent.Serf?.Members() ?? [];
        var membersJoiner = joinerAgent.Serf?.Members() ?? [];

        Assert.Equal(2, membersSeed.Length);
        Assert.Equal(2, membersJoiner.Length);

        lighthouseMock.Verify(c => c.DiscoverNodesAsync(
            It.IsAny<NodeInfo>(),
            versionName,
            versionNumber,
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // Cleanup
        await joinerAgent.ShutdownAsync();
        await seedAgent.ShutdownAsync();
        await joinerAgent.DisposeAsync();
        await seedAgent.DisposeAsync();
    }

    /// <summary>
    /// Live integration test against the hosted Lighthouse instance.
    /// Uses generated ECDSA and AES keys plus a random cluster id so no external
    /// configuration or secrets are required.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task HostedLighthouse_DiscoverNodes_Works_WhenConfigured()
    {
        // Generate ephemeral ECDSA key pair (P-256) and AES-256 key for this test run
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const int timeoutSeconds = 30;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = BaseUrl;
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = timeoutSeconds;
        });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILighthouseClient>();

        // Explicitly register the cluster with Lighthouse
        var registered = await client.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);
        Assert.True(registered);

        // The first node joins an empty cluster - should see no peers yet
        var node1 = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19021,
            Metadata = new Dictionary<string, string>
            {
                ["node"] = "one"
            }
        };

        var peers1 = await client.DiscoverNodesAsync(
            node1,
            versionName: "nserf-tests",
            versionNumber: 1,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(peers1);
        Assert.Empty(peers1); // The first node should not see any others yet

        // The second node joins - should see the first node as a peer
        var node2 = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19022,
            Metadata = new Dictionary<string, string>
            {
                ["node"] = "two"
            }
        };

        var peers2 = await client.DiscoverNodesAsync(
            node2,
            versionName: "nserf-tests",
            versionNumber: 1,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(peers2);
        Assert.Contains(peers2, p =>
            p.IpAddress == node1.IpAddress &&
            p.Port == node1.Port &&
            p.Metadata != null &&
            p.Metadata.TryGetValue("node", out var v) && v == "one");
    }

    [Fact(Timeout = 20000)]
    public async Task HostedLighthouse_SerfAgentStartJoin_Works()
    {
        // Generate ephemeral keys and cluster ID
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const int timeoutSeconds = 30;
        const string versionName = "nserf-agent-start";
        const long versionNumber = 1;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = BaseUrl;
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = timeoutSeconds;
        });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILighthouseClient>();

        var registered = await client.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);
        Assert.True(registered);

        // Start a seed agent (not using Lighthouse itself)
        var seedConfig = new AgentConfig
        {
            NodeName = "lh-seed-start",
            BindAddr = "127.0.0.1:19041"
        };
        var seedAgent = new SerfAgent(seedConfig);
        await seedAgent.StartAsync();

        // Register a seed node with Lighthouse using the client
        var seedNodeInfo = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19041,
            Metadata = new Dictionary<string, string> { ["node"] = "seed-start" }
        };

        var peersFromSeed = await client.DiscoverNodesAsync(
            seedNodeInfo,
            versionName,
            versionNumber,
            CancellationToken.None);

        Assert.NotNull(peersFromSeed);

        // Start a joiner agent that uses Lighthouse for start join
        var joinConfig = new AgentConfig
        {
            NodeName = "lh-joiner-start",
            BindAddr = "127.0.0.1:19042",
            UseLighthouseStartJoin = true,
            LighthouseVersionName = versionName,
            LighthouseVersionNumber = versionNumber
        };

        var joinerAgent = new SerfAgent(joinConfig, logger: null, lighthouseClient: client);
        await joinerAgent.StartAsync();

        // Allow gossip to propagate
        await Task.Delay(2000);

        var membersSeed = seedAgent.Serf?.Members() ?? [];
        var membersJoiner = joinerAgent.Serf?.Members() ?? [];

        Assert.Equal(2, membersSeed.Length);
        Assert.Equal(2, membersJoiner.Length);

        // Both nodes should see each other by name and be alive
        Assert.Contains(membersSeed, m => m is { Name: "lh-seed-start", Status: MemberStatus.Alive });
        Assert.Contains(membersSeed, m => m is { Name: "lh-joiner-start", Status: MemberStatus.Alive });

        Assert.Contains(membersJoiner, m => m is { Name: "lh-seed-start", Status: MemberStatus.Alive });
        Assert.Contains(membersJoiner, m => m is { Name: "lh-joiner-start", Status: MemberStatus.Alive });

        await joinerAgent.ShutdownAsync();
        await seedAgent.ShutdownAsync();
        await joinerAgent.DisposeAsync();
        await seedAgent.DisposeAsync();
    }

    [Fact(Timeout = 25000)]
    public async Task HostedLighthouse_SerfAgentRetryJoin_Works()
    {
        // Generate ephemeral keys and cluster ID
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");

        const int timeoutSeconds = 30;
        const string versionName = "nserf-agent-retry";
        const long versionNumber = 1;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = BaseUrl;
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = timeoutSeconds;
        });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILighthouseClient>();

        var registered = await client.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);
        Assert.True(registered);

        // Start the joiner agent first; it will retry using Lighthouse
        var joinConfig = new AgentConfig
        {
            NodeName = "lh-retry-joiner",
            BindAddr = "127.0.0.1:19052",
            RetryJoin = [],
            RetryInterval = TimeSpan.FromMilliseconds(500),
            RetryMaxAttempts = 20,
            UseLighthouseRetryJoin = true,
            LighthouseVersionName = versionName,
            LighthouseVersionNumber = versionNumber
        };

        var joinerAgent = new SerfAgent(joinConfig, logger: null, lighthouseClient: client);
        await joinerAgent.StartAsync();

        // Give the joiner a moment to start failing initial retries
        await Task.Delay(1000);

        // Now start the seed agent and register it with Lighthouse
        var seedConfig = new AgentConfig
        {
            NodeName = "lh-retry-seed",
            BindAddr = "127.0.0.1:19051"
        };
        var seedAgent = new SerfAgent(seedConfig);
        await seedAgent.StartAsync();

        var seedNodeInfo = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19051,
            Metadata = new Dictionary<string, string> { ["node"] = "seed-retry" }
        };

        var peersFromSeed = await client.DiscoverNodesAsync(
            seedNodeInfo,
            versionName,
            versionNumber,
            CancellationToken.None);

        Assert.NotNull(peersFromSeed);

        // Allow several retry intervals for the joiner to discover and join the seed
        await Task.Delay(8000);

        var membersSeed = seedAgent.Serf?.Members() ?? [];
        var membersJoiner = joinerAgent.Serf?.Members() ?? [];

        Assert.Equal(2, membersSeed.Length);
        Assert.Equal(2, membersJoiner.Length);

        await joinerAgent.ShutdownAsync();
        await seedAgent.ShutdownAsync();
        await joinerAgent.DisposeAsync();
        await seedAgent.DisposeAsync();
    }


    [Fact(Timeout = 10000)]
    public async Task StartJoin_WithoutLighthouseClient_StartsSuccessfully()
    {
        // Agent with Lighthouse join enabled but no ILighthouseClient injected should start without error
        var config = new AgentConfig
        {
            NodeName = "no-lighthouse-client",
            BindAddr = "127.0.0.1:19061",
            UseLighthouseStartJoin = true,
            LighthouseVersionName = "test",
            LighthouseVersionNumber = 1
        };

        var agent = new SerfAgent(config, logger: null, lighthouseClient: null);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);
        var members = agent.Serf.Members();
        Assert.Single(members); // Only itself

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task StartJoin_WithEmptyPeerList_StartsSuccessfully()
    {
        // Lighthouse returns empty peer list - agent should start without joining anyone
        var lighthouseMock = new Mock<ILighthouseClient>();
        lighthouseMock
            .Setup(c => c.DiscoverNodesAsync(
                It.IsAny<NodeInfo>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var config = new AgentConfig
        {
            NodeName = "empty-peers",
            BindAddr = "127.0.0.1:19062",
            UseLighthouseStartJoin = true,
            LighthouseVersionName = "test",
            LighthouseVersionNumber = 1
        };

        var agent = new SerfAgent(config, logger: null, lighthouseClient: lighthouseMock.Object);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);
        var members = agent.Serf.Members();
        Assert.Single(members); // Only itself

        lighthouseMock.Verify(c => c.DiscoverNodesAsync(
            It.IsAny<NodeInfo>(),
            "test",
            1,
            It.IsAny<CancellationToken>()), Times.Once);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task StartJoin_FiltersSelfFromPeerList()
    {
        // Lighthouse returns a peer list that includes the agent's own address - should be filtered out
        var lighthouseMock = new Mock<ILighthouseClient>();
        lighthouseMock
            .Setup(c => c.DiscoverNodesAsync(
                It.IsAny<NodeInfo>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NodeInfo
                {
                    IpAddress = "127.0.0.1",
                    Port = 19063, // Same as agent's port
                    Metadata = new Dictionary<string, string>()
                }
            ]);

        var config = new AgentConfig
        {
            NodeName = "self-filter",
            BindAddr = "127.0.0.1:19063",
            UseLighthouseStartJoin = true,
            LighthouseVersionName = "test",
            LighthouseVersionNumber = 1
        };

        var agent = new SerfAgent(config, logger: null, lighthouseClient: lighthouseMock.Object);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);
        var members = agent.Serf.Members();
        Assert.Single(members); // Only itself, self was filtered out

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task StartJoin_WithMissingVersionName_DoesNotCallLighthouse()
    {
        // Agent with a missing version name should not call Lighthouse
        var lighthouseMock = new Mock<ILighthouseClient>();

        var config = new AgentConfig
        {
            NodeName = "no-version-name",
            BindAddr = "127.0.0.1:19064",
            UseLighthouseStartJoin = true,
            LighthouseVersionName = "", // Empty version name
            LighthouseVersionNumber = 1
        };

        var agent = new SerfAgent(config, logger: null, lighthouseClient: lighthouseMock.Object);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);

        // Lighthouse should NOT be called
        lighthouseMock.Verify(c => c.DiscoverNodesAsync(
            It.IsAny<NodeInfo>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task StartJoin_WithInvalidVersionNumber_DoesNotCallLighthouse()
    {
        // Agent with invalid version number (<=0) should not call Lighthouse
        var lighthouseMock = new Mock<ILighthouseClient>();

        var config = new AgentConfig
        {
            NodeName = "invalid-version",
            BindAddr = "127.0.0.1:19065",
            UseLighthouseStartJoin = true,
            LighthouseVersionName = "test",
            LighthouseVersionNumber = 0 // Invalid version number
        };

        var agent = new SerfAgent(config, logger: null, lighthouseClient: lighthouseMock.Object);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);

        // Lighthouse should NOT be called
        lighthouseMock.Verify(c => c.DiscoverNodesAsync(
            It.IsAny<NodeInfo>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task RetryJoin_WithLighthouseFailure_ContinuesRetrying()
    {
        // Lighthouse throws exception initially, then succeeds - retry should handle gracefully
        var lighthouseMock = new Mock<ILighthouseClient>();
        var callCount = 0;
        const int seedPort = 19071;

        lighthouseMock
            .Setup(c => c.DiscoverNodesAsync(
                It.IsAny<NodeInfo>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 3)
                {
                    // The first few calls return empty (simulating failure/no peers)
                    return [];
                }
                // Later calls return the seed
                return
                [
                    new NodeInfo
                    {
                        IpAddress = "127.0.0.1",
                        Port = seedPort,
                        Metadata = new Dictionary<string, string>()
                    }
                ];
            });

        var joinerConfig = new AgentConfig
        {
            NodeName = "retry-failure-joiner",
            BindAddr = "127.0.0.1:19072",
            RetryJoin = [],
            RetryInterval = TimeSpan.FromMilliseconds(200),
            RetryMaxAttempts = 15,
            UseLighthouseRetryJoin = true,
            LighthouseVersionName = "test",
            LighthouseVersionNumber = 1
        };

        var joinerAgent = new SerfAgent(joinerConfig, logger: null, lighthouseClient: lighthouseMock.Object);
        await joinerAgent.StartAsync();

        // Start seed after a delay
        await Task.Delay(500);
        var seedConfig = new AgentConfig
        {
            NodeName = "retry-failure-seed",
            BindAddr = $"127.0.0.1:{seedPort}"
        };
        var seedAgent = new SerfAgent(seedConfig);
        await seedAgent.StartAsync();

        // Allow retries to succeed
        await Task.Delay(3000);

        var membersSeed = seedAgent.Serf?.Members() ?? [];
        var membersJoiner = joinerAgent.Serf?.Members() ?? [];

        Assert.Equal(2, membersSeed.Length);
        Assert.Equal(2, membersJoiner.Length);

        // Verify multiple retry attempts were made
        lighthouseMock.Verify(c => c.DiscoverNodesAsync(
            It.IsAny<NodeInfo>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(3));

        await joinerAgent.ShutdownAsync();
        await seedAgent.ShutdownAsync();
        await joinerAgent.DisposeAsync();
        await seedAgent.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task StartJoin_WithMultiplePeers_JoinsAllSuccessfully()
    {
        // Start two seed nodes and join them together
        var seed1Config = new AgentConfig
        {
            NodeName = "multi-seed-1",
            BindAddr = "127.0.0.1:19081"
        };
        var seed1Agent = new SerfAgent(seed1Config);
        await seed1Agent.StartAsync();

        var seed2Config = new AgentConfig
        {
            NodeName = "multi-seed-2",
            BindAddr = "127.0.0.1:19082",
            StartJoin = ["127.0.0.1:19081"] // Join seed1
        };
        var seed2Agent = new SerfAgent(seed2Config);
        await seed2Agent.StartAsync();

        // Allow seeds to form a cluster
        await Task.Delay(500);

        // Mock Lighthouse to return both seeds
        var lighthouseMock = new Mock<ILighthouseClient>();
        lighthouseMock
            .Setup(c => c.DiscoverNodesAsync(
                It.IsAny<NodeInfo>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NodeInfo
                {
                    IpAddress = "127.0.0.1",
                    Port = 19081,
                    Metadata = new Dictionary<string, string>()
                },
                new NodeInfo
                {
                    IpAddress = "127.0.0.1",
                    Port = 19082,
                    Metadata = new Dictionary<string, string>()
                }
            ]);

        var joinerConfig = new AgentConfig
        {
            NodeName = "multi-joiner",
            BindAddr = "127.0.0.1:19083",
            UseLighthouseStartJoin = true,
            LighthouseVersionName = "test",
            LighthouseVersionNumber = 1
        };

        var joinerAgent = new SerfAgent(joinerConfig, logger: null, lighthouseClient: lighthouseMock.Object);
        await joinerAgent.StartAsync();

        // Allow gossip to propagate across all nodes
        await Task.Delay(2000);

        var members = joinerAgent.Serf?.Members() ?? [];
        Assert.Equal(3, members.Length); // All three nodes

        Assert.Contains(members, m => m is { Name: "multi-seed-1", Status: MemberStatus.Alive });
        Assert.Contains(members, m => m is { Name: "multi-seed-2", Status: MemberStatus.Alive });
        Assert.Contains(members, m => m is { Name: "multi-joiner", Status: MemberStatus.Alive });

        await joinerAgent.ShutdownAsync();
        await seed2Agent.ShutdownAsync();
        await seed1Agent.ShutdownAsync();
        await joinerAgent.DisposeAsync();
        await seed2Agent.DisposeAsync();
        await seed1Agent.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task StartJoin_DisabledLighthouseJoin_DoesNotCallLighthouse()
    {
        // Agent with a Lighthouse client but UseLighthouseStartJoin = false
        var lighthouseMock = new Mock<ILighthouseClient>();

        var config = new AgentConfig
        {
            NodeName = "disabled-lighthouse",
            BindAddr = "127.0.0.1:19091",
            UseLighthouseStartJoin = false, // Disabled
            LighthouseVersionName = "test",
            LighthouseVersionNumber = 1
        };

        var agent = new SerfAgent(config, logger: null, lighthouseClient: lighthouseMock.Object);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);

        // Lighthouse should NOT be called
        lighthouseMock.Verify(c => c.DiscoverNodesAsync(
            It.IsAny<NodeInfo>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task RetryJoin_DisabledLighthouseRetry_DoesNotCallLighthouse()
    {
        // Agent with a Lighthouse client but UseLighthouseRetryJoin = false
        var lighthouseMock = new Mock<ILighthouseClient>();

        var config = new AgentConfig
        {
            NodeName = "disabled-retry",
            BindAddr = "127.0.0.1:19092",
            RetryJoin = [],
            RetryInterval = TimeSpan.FromMilliseconds(200),
            RetryMaxAttempts = 5,
            UseLighthouseRetryJoin = false, // Disabled
            LighthouseVersionName = "test",
            LighthouseVersionNumber = 1
        };

        var agent = new SerfAgent(config, logger: null, lighthouseClient: lighthouseMock.Object);
        await agent.StartAsync();

        // Wait for potential retry attempts
        await Task.Delay(1500);

        // Lighthouse should NOT be called for retry
        lighthouseMock.Verify(c => c.DiscoverNodesAsync(
            It.IsAny<NodeInfo>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task StartJoin_CombinesLighthouseAndStaticPeers()
    {
        // Start two seed nodes and join them together
        var seed1Config = new AgentConfig
        {
            NodeName = "combined-seed-1",
            BindAddr = "127.0.0.1:19101"
        };
        var seed1Agent = new SerfAgent(seed1Config);
        await seed1Agent.StartAsync();

        var seed2Config = new AgentConfig
        {
            NodeName = "combined-seed-2",
            BindAddr = "127.0.0.1:19102",
            StartJoin = ["127.0.0.1:19101"] // Join seed1
        };
        var seed2Agent = new SerfAgent(seed2Config);
        await seed2Agent.StartAsync();

        // Allow seeds to form a cluster
        await Task.Delay(500);

        // Mock Lighthouse to return only seed1
        var lighthouseMock = new Mock<ILighthouseClient>();
        lighthouseMock
            .Setup(c => c.DiscoverNodesAsync(
                It.IsAny<NodeInfo>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NodeInfo
                {
                    IpAddress = "127.0.0.1",
                    Port = 19101,
                    Metadata = new Dictionary<string, string>()
                }
            ]);

        // Configure a joiner with both Lighthouse and static StartJoin
        var joinerConfig = new AgentConfig
        {
            NodeName = "combined-joiner",
            BindAddr = "127.0.0.1:19103",
            StartJoin = ["127.0.0.1:19102"], // Static join to seed2
            UseLighthouseStartJoin = true, // Also use Lighthouse for seed1
            LighthouseVersionName = "test",
            LighthouseVersionNumber = 1
        };

        var joinerAgent = new SerfAgent(joinerConfig, logger: null, lighthouseClient: lighthouseMock.Object);
        await joinerAgent.StartAsync();

        // Allow gossip to propagate across all nodes
        await Task.Delay(2000);

        var members = joinerAgent.Serf?.Members() ?? [];
        Assert.Equal(3, members.Length); // All three nodes

        Assert.Contains(members, m => m is { Name: "combined-seed-1", Status: MemberStatus.Alive });
        Assert.Contains(members, m => m is { Name: "combined-seed-2", Status: MemberStatus.Alive });
        Assert.Contains(members, m => m is { Name: "combined-joiner", Status: MemberStatus.Alive });

        await joinerAgent.ShutdownAsync();
        await seed2Agent.ShutdownAsync();
        await seed1Agent.ShutdownAsync();
        await joinerAgent.DisposeAsync();
        await seed2Agent.DisposeAsync();
        await seed1Agent.DisposeAsync();
    }


    [Fact(Timeout = 20000)]
    public async Task HostedLighthouse_MultipleNodesWithMetadata_DiscoverCorrectly()
    {
        // Test that nodes can discover each other with rich metadata
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const int timeoutSeconds = 30;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = BaseUrl;
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = timeoutSeconds;
        });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILighthouseClient>();

        var registered = await client.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);
        Assert.True(registered);

        // Register three nodes with different metadata
        var node1 = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19111,
            Metadata = new Dictionary<string, string>
            {
                ["role"] = "master",
                ["region"] = "us-east",
                ["version"] = "1.0.0"
            }
        };

        var node2 = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19112,
            Metadata = new Dictionary<string, string>
            {
                ["role"] = "worker",
                ["region"] = "us-west",
                ["version"] = "1.0.0"
            }
        };

        var node3 = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19113,
            Metadata = new Dictionary<string, string>
            {
                ["role"] = "worker",
                ["region"] = "us-east",
                ["version"] = "1.0.1"
            }
        };

        // Register all nodes
        await client.DiscoverNodesAsync(node1, "metadata-test", 1, CancellationToken.None);
        await client.DiscoverNodesAsync(node2, "metadata-test", 1, CancellationToken.None);
        await client.DiscoverNodesAsync(node3, "metadata-test", 1, CancellationToken.None);

        // The fourth node discovers all three
        var node4 = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19114,
            Metadata = new Dictionary<string, string>
            {
                ["role"] = "observer",
                ["region"] = "eu-central"
            }
        };

        var discoveredPeers = await client.DiscoverNodesAsync(node4, "metadata-test", 1, CancellationToken.None);

        Assert.NotNull(discoveredPeers);
        Assert.Equal(3, discoveredPeers.Count);

        // Verify all nodes are discovered with correct metadata
        var masterNode = discoveredPeers.FirstOrDefault(p => p.Port == 19111);
        Assert.NotNull(masterNode);
        Assert.Equal("master", masterNode.Metadata?["role"]);
        Assert.Equal("us-east", masterNode.Metadata?["region"]);

        var workerUsWest = discoveredPeers.FirstOrDefault(p => p.Port == 19112);
        Assert.NotNull(workerUsWest);
        Assert.Equal("worker", workerUsWest.Metadata?["role"]);
        Assert.Equal("us-west", workerUsWest.Metadata?["region"]);

        var workerUsEast = discoveredPeers.FirstOrDefault(p => p.Port == 19113);
        Assert.NotNull(workerUsEast);
        Assert.Equal("worker", workerUsEast.Metadata?["role"]);
        Assert.Equal("1.0.1", workerUsEast.Metadata?["version"]);
    }

    [Fact(Timeout = 25000)]
    public async Task HostedLighthouse_VersionIsolation_OnlyDiscoversSameVersion()
    {
        // Test that nodes with different version names don't discover each other
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = BaseUrl;
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILighthouseClient>();

        await client.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);

        // Register nodes with version "v1"
        var v1Node1 = new NodeInfo { IpAddress = "127.0.0.1", Port = 19121, Metadata = new Dictionary<string, string>() };
        var v1Node2 = new NodeInfo { IpAddress = "127.0.0.1", Port = 19122, Metadata = new Dictionary<string, string>() };

        await client.DiscoverNodesAsync(v1Node1, "version-v1", 1, CancellationToken.None);
        await client.DiscoverNodesAsync(v1Node2, "version-v1", 1, CancellationToken.None);

        // Register nodes with version "v2"
        var v2Node1 = new NodeInfo { IpAddress = "127.0.0.1", Port = 19123, Metadata = new Dictionary<string, string>() };
        var v2Node2 = new NodeInfo { IpAddress = "127.0.0.1", Port = 19124, Metadata = new Dictionary<string, string>() };

        await client.DiscoverNodesAsync(v2Node1, "version-v2", 1, CancellationToken.None);
        await client.DiscoverNodesAsync(v2Node2, "version-v2", 1, CancellationToken.None);

        // The new v1 node should only see other v1 nodes
        var v1Node3 = new NodeInfo { IpAddress = "127.0.0.1", Port = 19125, Metadata = new Dictionary<string, string>() };
        var v1Peers = await client.DiscoverNodesAsync(v1Node3, "version-v1", 1, CancellationToken.None);

        Assert.NotNull(v1Peers);
        Assert.Equal(2, v1Peers.Count);
        Assert.All(v1Peers, p => Assert.True(p.Port is 19121 or 19122));

        // The new v2 node should only see other v2 nodes
        var v2Node3 = new NodeInfo { IpAddress = "127.0.0.1", Port = 19126, Metadata = new Dictionary<string, string>() };
        var v2Peers = await client.DiscoverNodesAsync(v2Node3, "version-v2", 1, CancellationToken.None);

        Assert.NotNull(v2Peers);
        Assert.Equal(2, v2Peers.Count);
        Assert.All(v2Peers, p => Assert.True(p.Port is 19123 or 19124));
    }

    [Fact(Timeout = 30000)]
    public async Task HostedLighthouse_LargeCluster_HandlesMultipleNodes()
    {
        // Test discovery with multiple nodes (5 nodes to stay within server limits)
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = BaseUrl;
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILighthouseClient>();

        await client.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);

        // Register 5 nodes (server may limit results)
        const int nodeCount = 5;
        const int basePort = 19131;

        for (int i = 0; i < nodeCount; i++)
        {
            var node = new NodeInfo
            {
                IpAddress = "127.0.0.1",
                Port = basePort + i,
                Metadata = new Dictionary<string, string>
                {
                    ["index"] = i.ToString(),
                    ["group"] = (i % 3).ToString() // Distribute across 3 groups
                }
            };

            await client.DiscoverNodesAsync(node, "large-cluster", 1, CancellationToken.None);
        }

        // New node discovers all 10
        var newNode = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = basePort + nodeCount,
            Metadata = new Dictionary<string, string>()
        };

        var peers = await client.DiscoverNodesAsync(newNode, "large-cluster", 1, CancellationToken.None);

        Assert.NotNull(peers);
        Assert.True(peers.Count is >= 1 and <= nodeCount, $"Expected 1-{nodeCount} peers, got {peers.Count}");

        // Verify discovered ports are within the expected range
        var discoveredPorts = peers.Select(p => p.Port).OrderBy(p => p).ToList();
        Assert.All(discoveredPorts, port => Assert.InRange(port, basePort, basePort + nodeCount - 1));

        // Verify metadata is preserved
        foreach (var peer in peers)
        {
            Assert.NotNull(peer.Metadata);
            Assert.True(peer.Metadata.ContainsKey("index"));
            Assert.True(peer.Metadata.ContainsKey("group"));
        }
    }

    [Fact(Timeout = 25000)]
    public async Task HostedLighthouse_SerfAgentWithDynamicCluster_JoinsSuccessfully()
    {
        // Test real SerfAgent joining a dynamically growing cluster via Lighthouse
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");
        const string versionName = "dynamic-cluster";
        const long versionNumber = 1;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = BaseUrl;
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILighthouseClient>();

        await client.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);

        // Start the first seed node
        var seed1Config = new AgentConfig
        {
            NodeName = "dynamic-seed-1",
            BindAddr = "127.0.0.1:19141"
        };
        var seed1Agent = new SerfAgent(seed1Config);
        await seed1Agent.StartAsync();

        // Register with Lighthouse
        await client.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "127.0.0.1", Port = 19141, Metadata = new Dictionary<string, string> { ["role"] = "seed" } },
            versionName,
            versionNumber,
            CancellationToken.None);

        await Task.Delay(500);

        // The second node joins via Lighthouse
        var joiner1Config = new AgentConfig
        {
            NodeName = "dynamic-joiner-1",
            BindAddr = "127.0.0.1:19142",
            UseLighthouseStartJoin = true,
            LighthouseVersionName = versionName,
            LighthouseVersionNumber = versionNumber
        };
        var joiner1Agent = new SerfAgent(joiner1Config, logger: null, lighthouseClient: client);
        await joiner1Agent.StartAsync();

        await Task.Delay(1000);

        // Verify 2-node cluster
        var members1 = joiner1Agent.Serf?.Members() ?? [];
        Assert.Equal(2, members1.Length);

        // Register joiner1 with Lighthouse
        await client.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "127.0.0.1", Port = 19142, Metadata = new Dictionary<string, string> { ["role"] = "worker" } },
            versionName,
            versionNumber,
            CancellationToken.None);

        await Task.Delay(500);

        // The third node joins via Lighthouse and should discover both previous nodes
        var joiner2Config = new AgentConfig
        {
            NodeName = "dynamic-joiner-2",
            BindAddr = "127.0.0.1:19143",
            UseLighthouseStartJoin = true,
            LighthouseVersionName = versionName,
            LighthouseVersionNumber = versionNumber
        };
        var joiner2Agent = new SerfAgent(joiner2Config, logger: null, lighthouseClient: client);
        await joiner2Agent.StartAsync();

        await Task.Delay(2000);

        // Verify a 3-node cluster from all perspectives
        var membersSeed = seed1Agent.Serf?.Members() ?? [];
        var membersJoiner1 = joiner1Agent.Serf?.Members() ?? [];
        var membersJoiner2 = joiner2Agent.Serf?.Members() ?? [];

        Assert.Equal(3, membersSeed.Length);
        Assert.Equal(3, membersJoiner1.Length);
        Assert.Equal(3, membersJoiner2.Length);

        // Verify all nodes see each other
        Assert.Contains(membersSeed, m => m is { Name: "dynamic-seed-1", Status: MemberStatus.Alive });
        Assert.Contains(membersSeed, m => m is { Name: "dynamic-joiner-1", Status: MemberStatus.Alive });
        Assert.Contains(membersSeed, m => m is { Name: "dynamic-joiner-2", Status: MemberStatus.Alive });

        await joiner2Agent.ShutdownAsync();
        await joiner1Agent.ShutdownAsync();
        await seed1Agent.ShutdownAsync();
        await joiner2Agent.DisposeAsync();
        await joiner1Agent.DisposeAsync();
        await seed1Agent.DisposeAsync();
    }

    [Fact(Timeout = 20000)]
    public async Task HostedLighthouse_NodeReregistration_UpdatesMetadata()
    {
        // Test that re-registering a node updates its metadata
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var aesKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(aesKeyBytes);
        var aesKey = Convert.ToBase64String(aesKeyBytes);

        var clusterId = Guid.NewGuid().ToString("D");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = BaseUrl;
            options.ClusterId = clusterId;
            options.PrivateKey = privateKey;
            options.AesKey = aesKey;
            options.TimeoutSeconds = 30;
        });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILighthouseClient>();

        await client.RegisterClusterAsync(publicKeyBytes, CancellationToken.None);

        // Register a node with initial metadata
        var node1 = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19151,
            Metadata = new Dictionary<string, string>
            {
                ["status"] = "initializing",
                ["load"] = "0"
            }
        };

        await client.DiscoverNodesAsync(node1, "reregister-test", 1, CancellationToken.None);

        // Re-register the same node with updated metadata
        var node1Updated = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19151,
            Metadata = new Dictionary<string, string>
            {
                ["status"] = "ready",
                ["load"] = "75",
                ["uptime"] = "3600"
            }
        };

        await client.DiscoverNodesAsync(node1Updated, "reregister-test", 1, CancellationToken.None);

        // Another node discovers it with updated metadata
        var node2 = new NodeInfo
        {
            IpAddress = "127.0.0.1",
            Port = 19152,
            Metadata = new Dictionary<string, string>()
        };

        var peers = await client.DiscoverNodesAsync(node2, "reregister-test", 1, CancellationToken.None);

        Assert.NotNull(peers);
        Assert.True(peers.Count >= 1, "Should discover at least one node");

        // Server may return multiple entries (history), find the most recent one with uptime
        var discoveredNode = peers.FirstOrDefault(p => p.Port == 19151 && p.Metadata?.ContainsKey("uptime") == true)
                             ?? peers.First(p => p.Port == 19151);

        Assert.Equal(19151, discoveredNode.Port);
        Assert.NotNull(discoveredNode.Metadata);
        Assert.Equal("ready", discoveredNode.Metadata["status"]);
        Assert.Equal("75", discoveredNode.Metadata["load"]);
        Assert.Equal("3600", discoveredNode.Metadata["uptime"]);
    }

}
