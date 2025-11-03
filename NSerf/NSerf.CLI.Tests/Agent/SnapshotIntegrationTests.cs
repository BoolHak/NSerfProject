// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.CLI.Tests.Fixtures;
using NSerf.Serf;
using NSerf.CLI.Tests.Commands;

namespace NSerf.CLI.Tests.Agent;

/// <summary>
/// Tests for snapshot functionality
/// Ported from Go's agent tests
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public class SnapshotIntegrationTests : IAsyncLifetime
{
    private string? _tempDir;

    public Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"serf-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_tempDir != null && Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        return Task.CompletedTask;
    }

    [Fact(Timeout = 15000)]
    public async Task Agent_CreatesSnapshot_OnShutdown()
    {
        var snapshotPath = Path.Combine(_tempDir!, $"{Guid.NewGuid()}-snapshot.db");
        var config = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RpcAddr = "",
            SnapshotPath = snapshotPath
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();
        
        // Verify agent started
        Assert.NotNull(agent.Serf);
        await Task.Delay(1500);
        await agent.ShutdownAsync();
        await Task.Delay(3000);

        // Snapshot should be created on shutdown
        Assert.True(File.Exists(snapshotPath), "Snapshot file should be created");
    }

    [Fact(Timeout = 20000)]
    public async Task Agent_RestoresFromSnapshot_AfterRestart()
    {
        var snapshotPath = Path.Combine(_tempDir!, "snapshot-restore.db");
        var nodeName = TestHelper.GetRandomNodeName();
        var bindAddr = TestHelper.GetRandomBindAddr();
        
        // Create agent with snapshot
        var config1 = new AgentConfig
        {
            NodeName = nodeName,
            BindAddr = bindAddr,
            RpcAddr = "",
            SnapshotPath = snapshotPath
        };

        await using var agent1 = new SerfAgent(config1);
        await agent1.StartAsync();
        
        var originalMembers = agent1.Serf!.Members();
        Assert.Single(originalMembers);
        
        await agent1.ShutdownAsync();

        await Task.Delay(5000);

        // Restart with same snapshot
        var config2 = new AgentConfig
        {
            NodeName = nodeName,
            BindAddr = bindAddr,
            RpcAddr = "",
            SnapshotPath = snapshotPath,
            RejoinAfterLeave = true
        };

        await using var agent2 = new SerfAgent(config2);
        await agent2.StartAsync();
        
        var restoredMembers = agent2.Serf!.Members();
        Assert.Single(restoredMembers);
        Assert.Equal(nodeName, restoredMembers[0].Name);
    }

    [Fact(Timeout = 30000)]
    public async Task Agent_SnapshotContainsMemberInfo_AfterJoin()
    {
        var snapshotPath = Path.Combine(_tempDir!, "snapshot-members.db");
        
        await using var agent1 = new AgentFixture();
        await agent1.InitializeAsync();

        var config2 = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RpcAddr = "",
            SnapshotPath = snapshotPath
        };

        await using var agent2 = new SerfAgent(config2);
        await agent2.StartAsync();

        // Join the cluster
        var joinAddr = $"{agent1.Agent!.Serf!.Members()[0].Addr}:{agent1.Agent.Serf.Members()[0].Port}";
        await agent2.Serf!.JoinAsync(new[] { joinAddr }, ignoreOld: false);
        await Task.Delay(2000);

        var members = agent2.Serf.Members();
        Assert.Equal(2, members.Length);

        await agent2.ShutdownAsync();

        // Snapshot should exist with member data
        Assert.True(File.Exists(snapshotPath));
        
        // Verify snapshot is not empty
        var fileInfo = new FileInfo(snapshotPath);
        Assert.True(fileInfo.Length > 0, "Snapshot should contain data");
    }

    [Fact(Timeout = 15000)]
    public async Task Agent_NoSnapshotPath_SkipsSnapshot()
    {
        var config = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RpcAddr = ""
            // No SnapshotPath specified
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();
        
        Assert.NotNull(agent.Serf);
        
        await agent.ShutdownAsync();

        // No snapshot file should be created
        var files = Directory.GetFiles(_tempDir!);
        Assert.Empty(files);
    }

    [Fact(Timeout = 20000)]
    public async Task Agent_CorruptSnapshot_StartsClean()
    {
        var snapshotPath = Path.Combine(_tempDir!, "corrupt.db");
        
        // Create corrupt snapshot file
        await File.WriteAllTextAsync(snapshotPath, "THIS IS NOT A VALID SNAPSHOT");

        var config = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RpcAddr = "",
            SnapshotPath = snapshotPath
        };

        // Should start successfully despite corrupt snapshot
        await using var agent = new SerfAgent(config);
        await agent.StartAsync();
        
        Assert.NotNull(agent.Serf);
        var members = agent.Serf.Members();
        Assert.Single(members); // Only local node
    }
}
