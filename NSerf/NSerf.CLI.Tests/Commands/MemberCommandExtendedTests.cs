// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.CLI.Tests.Fixtures;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Integration")]
[Collection("Sequential")]
public class MemberCommandExtendedTests : IAsyncLifetime
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
    public void Members_ReturnsLocalNode()
    {
        var members = _fixture!.Agent!.Serf!.Members();
        Assert.Single(members);
        Assert.Equal(_fixture.Agent.NodeName, members[0].Name);
    }

    [Fact(Timeout = 15000)]
    public async Task Members_MultipleNodes_ReturnsAll()
    {
        await using var agent2 = new AgentFixture();
        await agent2.InitializeAsync();

        var addr = $"{agent2.Agent!.Serf!.Members()[0].Addr}:{agent2.Agent.Serf.Members()[0].Port}";
        await _fixture!.Agent!.Serf!.JoinAsync(new[] { addr }, ignoreOld: false);
        await Task.Delay(2000);

        var members = _fixture.Agent.Serf.Members();
        Assert.Equal(2, members.Length);
    }

    [Fact(Timeout = 15000)]
    public void Members_FilterByStatus_Alive()
    {
        var members = _fixture!.Agent!.Serf!.Members();
        var aliveMembers = members.Where(m => m.Status == Serf.MemberStatus.Alive).ToArray();
        
        Assert.Single(aliveMembers);
        Assert.Equal(_fixture.Agent.NodeName, aliveMembers[0].Name);
    }

    [Fact(Timeout = 15000)]
    public void Members_FilterByName()
    {
        var nodeName = _fixture!.Agent!.NodeName;
        var members = _fixture.Agent.Serf!.Members();
        var filtered = members.Where(m => m.Name == nodeName).ToArray();
        
        Assert.Single(filtered);
    }

    [Fact(Timeout = 15000)]
    public void Members_FilterByTags()
    {
        var members = _fixture!.Agent!.Serf!.Members();
        var withTags = members.Where(m => m.Tags.ContainsKey("role")).ToArray();
        
        Assert.Single(withTags);
        Assert.Equal("test", withTags[0].Tags["role"]);
    }

    [Fact(Timeout = 20000)]
    public async Task Members_AfterLeave_ShowsLeft()
    {
        await using var agent2 = new AgentFixture();
        await agent2.InitializeAsync();

        var addr = $"{agent2.Agent!.Serf!.Members()[0].Addr}:{agent2.Agent.Serf.Members()[0].Port}";
        await _fixture!.Agent!.Serf!.JoinAsync(new[] { addr }, ignoreOld: false);
        await Task.Delay(2000);

        await agent2.Agent.Serf.LeaveAsync();
        await Task.Delay(2000);

        var members = _fixture.Agent.Serf.Members();
        var leftMember = members.FirstOrDefault(m => m.Name == agent2.Agent.NodeName);
        Assert.NotNull(leftMember);
        Assert.Equal(Serf.MemberStatus.Left, leftMember.Status);
    }

    [Fact(Timeout = 20000)]
    public async Task Members_AfterGracefulShutdown_ShowsLeft()
    {
        await using var agent2 = new AgentFixture();
        await agent2.InitializeAsync();

        var addr = $"{agent2.Agent!.Serf!.Members()[0].Addr}:{agent2.Agent.Serf.Members()[0].Port}";
        await _fixture!.Agent!.Serf!.JoinAsync(new[] { addr }, ignoreOld: false);
        await Task.Delay(2000);

        // Graceful shutdown broadcasts leave message
        await agent2.Agent.ShutdownAsync();
        await Task.Delay(5000); // Reduced wait time since leave is now broadcasted correctly

        var members = _fixture.Agent.Serf.Members();
        var leftMember = members.FirstOrDefault(m => m.Name == agent2.Agent.NodeName);
        Assert.NotNull(leftMember);
        // Graceful shutdown should result in Left status (our leave fix ensures this)
        Assert.Equal(Serf.MemberStatus.Left, leftMember.Status);
    }

    [Fact(Timeout = 10000)]
    public void Members_ChecksProtocol()
    {
        var members = _fixture!.Agent!.Serf!.Members();
        Assert.Single(members);
        Assert.True(members[0].ProtocolMax >= 2);
        Assert.True(members[0].ProtocolMin <= 5);
    }

    [Fact(Timeout = 10000)]
    public void Members_ChecksDelegateVersions()
    {
        var members = _fixture!.Agent!.Serf!.Members();
        Assert.Single(members);
        Assert.True(members[0].DelegateMax >= members[0].DelegateMin);
        Assert.True(members[0].DelegateMin >= 0);
    }
}
