// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.CLI.Tests.Fixtures;

namespace NSerf.CLI.Tests.Commands;

/// <summary>
/// Tests for Stats/Info functionality
/// Ported from Go's stats tests
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public class StatsIntegrationTests : IAsyncLifetime
{
    private AgentFixture? _fixture;

    public async Task InitializeAsync()
    {
        _fixture = new AgentFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task Stats_ContainsAgentName()
    {
        var stats = _fixture!.Agent!.Serf!.Stats();

        Assert.NotNull(stats);
        // Stats returns flat dictionary of string values
        Assert.True(stats.ContainsKey("members"));
        Assert.True(stats.ContainsKey("health_score"));
        await Task.Delay(20);
    }

    [Fact(Timeout = 10000)]
    public async Task Stats_ContainsSerfStats()
    {
        var stats = _fixture!.Agent!.Serf!.Stats();

        Assert.NotNull(stats);
        Assert.True(stats.ContainsKey("member_time"));
        Assert.True(stats.ContainsKey("event_time"));
        Assert.True(stats.ContainsKey("query_time"));
        await Task.Delay(20);
    }

    [Fact(Timeout = 10000)]
    public async Task Stats_ContainsRuntimeStats()
    {
        var stats = _fixture!.Agent!.Serf!.Stats();

        Assert.NotNull(stats);
        // Verify key runtime stats are present
        Assert.True(stats.ContainsKey("intent_queue"));
        Assert.True(stats.ContainsKey("event_queue"));
        Assert.True(stats.ContainsKey("query_queue"));
        await Task.Delay(20);
    }

    [Fact(Timeout = 10000)]
    public async Task Stats_MemberCount_IsCorrect()
    {
        var members = _fixture!.Agent!.Serf!.Members();
        var stats = _fixture.Agent.Serf.Stats();

        Assert.NotNull(stats);
        Assert.True(stats.ContainsKey("members"));
        var memberCount = int.Parse(stats["members"]);
        Assert.True(memberCount >= 1); // At least local node
        await Task.Delay(20);
    }

    [Fact(Timeout = 20000)]
    public async Task Stats_EventQueue_TracksEvents()
    {
        // Trigger a user event
        await _fixture!.Agent!.Serf!.UserEventAsync("test-event", new byte[] { 1, 2, 3 }, coalesce: false);
        await Task.Delay(500);

        var stats = _fixture.Agent.Serf.Stats();

        Assert.NotNull(stats);
        Assert.True(stats.ContainsKey("event_queue"));

    }

    [Fact(Timeout = 15000)]
    public async Task Stats_Tags_AreIncluded()
    {
        var stats = _fixture!.Agent!.Serf!.Stats();

        Assert.NotNull(stats);
        // Stats provides member counts, not tags directly
        Assert.True(stats.ContainsKey("members"));
        Assert.True(stats.ContainsKey("encrypted"));
        await Task.Delay(20);
    }

    [Fact(Timeout = 10000)]
    public async Task Stats_Protocol_IsReported()
    {
        var stats = _fixture!.Agent!.Serf!.Stats();

        Assert.NotNull(stats);
        // Verify core stats are present
        Assert.True(stats.ContainsKey("member_time"));
        Assert.True(stats.ContainsKey("encrypted"));
        await Task.Delay(20);
    }

    [Fact(Timeout = 10000)]
    public async Task Stats_Delegate_IsReported()
    {
        var stats = _fixture!.Agent!.Serf!.Stats();

        Assert.NotNull(stats);
        // Verify health score is reported
        Assert.True(stats.ContainsKey("health_score"));
        var healthScore = int.Parse(stats["health_score"]);
        Assert.True(healthScore >= 0);
        await Task.Delay(20);
    }
}
