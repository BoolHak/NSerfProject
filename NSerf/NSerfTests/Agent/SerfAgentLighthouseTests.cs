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
}
