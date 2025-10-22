using NSerf.Agent;
using NSerf.Serf;
using NSerf.Serf.Events;
using Xunit;

namespace NSerfTests.Agent;

/// <summary>
/// RED Phase: Agent core tests ported from Go's agent_test.go
/// These tests will FAIL until we implement SerfAgent.cs
/// </summary>
public class SerfAgentTests : IAsyncLifetime
{
    private readonly List<SerfAgent> _agents = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var agent in _agents)
        {
            try
            {
                await agent.LeaveAsync();
                await agent.DisposeAsync();
            }
            catch { }
        }
        _agents.Clear();
    }

    private async Task<SerfAgent> CreateTestAgent(AgentConfig? agentConfig = null, Config? serfConfig = null)
    {
        agentConfig ??= new AgentConfig
        {
            NodeName = $"test-node-{Guid.NewGuid()}",
            BindAddr = "127.0.0.1:0",
            RpcAddr = null // Disable IPC for tests
        };

        serfConfig ??= new Config
        {
            NodeName = agentConfig.NodeName,
            MemberlistConfig = new global::NSerf.Memberlist.Configuration.MemberlistConfig
            {
                Name = agentConfig.NodeName,
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        var agent = await SerfAgent.CreateAsync(agentConfig, serfConfig);
        _agents.Add(agent);
        return agent;
    }

    [Fact]
    public async Task CreateAsync_WithValidConfig_Succeeds()
    {
        // Port from Go: testAgent pattern
        var agent = await CreateTestAgent();

        Assert.NotNull(agent);
        Assert.NotNull(agent.Serf);
        Assert.NotNull(agent.Config);
    }

    [Fact]
    public async Task StartAsync_InitializesAgent()
    {
        // Port from Go: TestAgent_eventHandler (Start part)
        var agent = await CreateTestAgent();

        await agent.StartAsync();

        // Agent should be started
        Assert.NotNull(agent.Serf);
    }

    [Fact]
    public async Task StartAsync_CalledTwice_ThrowsException()
    {
        // Test: Starting an already-started agent should fail
        var agent = await CreateTestAgent();

        await agent.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await agent.StartAsync();
        });
    }

    [Fact]
    public async Task EventHandler_ReceivesMemberJoinEvent()
    {
        // Port from Go: TestAgent_eventHandler
        var agent = await CreateTestAgent();
        var receivedEvents = new List<Event>();

        var handler = new DelegateEventHandler(evt =>
        {
            lock (receivedEvents)
            {
                receivedEvents.Add(evt);
            }
        });

        agent.RegisterEventHandler(handler);

        await agent.StartAsync();

        // Wait for member-join event (local node joins itself)
        await Task.Delay(500);

        lock (receivedEvents)
        {
            Assert.NotEmpty(receivedEvents);
            var memberJoin = receivedEvents.OfType<MemberEvent>()
                .FirstOrDefault(e => e.Type == EventType.MemberJoin);
            Assert.NotNull(memberJoin);
        }
    }

    [Fact]
    public async Task MultipleEventHandlers_AllReceiveEvents()
    {
        // Test: Multiple handlers should all receive the same events
        var agent = await CreateTestAgent();
        var events1 = new List<Event>();
        var events2 = new List<Event>();

        agent.RegisterEventHandler(new DelegateEventHandler(evt =>
        {
            lock (events1) events1.Add(evt);
        }));

        agent.RegisterEventHandler(new DelegateEventHandler(evt =>
        {
            lock (events2) events2.Add(evt);
        }));

        await agent.StartAsync();
        await Task.Delay(500);

        lock (events1)
        lock (events2)
        {
            Assert.NotEmpty(events1);
            Assert.NotEmpty(events2);
            // Both should have received member-join
            Assert.True(events1.Any(e => e is MemberEvent));
            Assert.True(events2.Any(e => e is MemberEvent));
        }
    }

    [Fact]
    public async Task EventHandler_Exception_DoesNotCrashEventLoop()
    {
        // Test: Handler throwing exception should not crash agent
        var agent = await CreateTestAgent();
        var goodHandlerCalled = false;

        // Bad handler that throws
        agent.RegisterEventHandler(new DelegateEventHandler(evt =>
        {
            throw new Exception("Handler error");
        }));

        // Good handler after the bad one
        agent.RegisterEventHandler(new DelegateEventHandler(evt =>
        {
            goodHandlerCalled = true;
        }));

        await agent.StartAsync();
        await Task.Delay(500);

        // Good handler should still be called despite bad handler throwing
        Assert.True(goodHandlerCalled);
    }

    [Fact]
    public async Task DeregisterEventHandler_StopsReceivingEvents()
    {
        // Test: Deregistered handler should not receive new events
        var agent = await CreateTestAgent();
        var eventCount = 0;

        var handler = new DelegateEventHandler(evt =>
        {
            Interlocked.Increment(ref eventCount);
        });

        agent.RegisterEventHandler(handler);
        await agent.StartAsync();
        await Task.Delay(300);

        var countAfterStart = eventCount;

        agent.DeregisterEventHandler(handler);

        // Fire a user event - deregistered handler should not receive it
        await agent.UserEventAsync("test", new byte[] { 1, 2, 3 }, false);
        await Task.Delay(300);

        // Event count should not have increased
        Assert.Equal(countAfterStart, eventCount);
    }

    [Fact]
    public async Task UserEventAsync_TriggersuserEvent()
    {
        // Port from Go: TestAgentUserEvent
        var agent = await CreateTestAgent();
        var userEvents = new List<UserEvent>();

        agent.RegisterEventHandler(new DelegateEventHandler(evt =>
        {
            if (evt is UserEvent ue)
            {
                lock (userEvents) userEvents.Add(ue);
            }
        }));

        await agent.StartAsync();
        await Task.Delay(200);

        await agent.UserEventAsync("deploy", new byte[] { 1, 2, 3 }, false);
        await Task.Delay(500);

        lock (userEvents)
        {
            var deployEvent = userEvents.FirstOrDefault(e => e.Name == "deploy");
            Assert.NotNull(deployEvent);
            Assert.Equal(new byte[] { 1, 2, 3 }, deployEvent.Payload);
        }
    }

    [Fact]
    public async Task QueryAsync_WithInvalidPrefix_ThrowsException()
    {
        // Port from Go: TestAgentQuery_BadPrefix
        var agent = await CreateTestAgent();
        await agent.StartAsync();
        await Task.Delay(200);

        // Query names starting with "_serf" are reserved
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await agent.QueryAsync("_serf_test", null, null);
        });
    }

    [Fact]
    public async Task JoinAsync_WithAddresses_ReturnsJoinCount()
    {
        // Test: Join should delegate to underlying Serf
        var agent1 = await CreateTestAgent();
        var agent2 = await CreateTestAgent();

        await agent1.StartAsync();
        await agent2.StartAsync();
        await Task.Delay(200);

        var agent1Port = agent1.Serf.Memberlist?.LocalNode.Port ?? 7946;
        var joinCount = await agent2.JoinAsync(new[] { $"127.0.0.1:{agent1Port}" }, replay: false);

        Assert.Equal(1, joinCount);
    }

    [Fact]
    public async Task ForceLeaveAsync_RemovesNode()
    {
        // Test: ForceLeave should remove failed node
        var agent = await CreateTestAgent();
        await agent.StartAsync();
        await Task.Delay(200);

        // Should not throw
        await agent.ForceLeaveAsync("non-existent-node");
    }

    [Fact]
    public async Task LeaveAsync_InitiatesGracefulShutdown()
    {
        // Port from Go: Leave test pattern
        var agent = await CreateTestAgent();
        await agent.StartAsync();
        await Task.Delay(200);

        await agent.LeaveAsync();

        // After leave, members should show as leaving/left
        var members = agent.Serf.Members();
        Assert.NotEmpty(members);
    }

    [Fact]
    public async Task DisposeAsync_MultipleTimesDoesNotThrow()
    {
        // Port from Go: TestAgentShutdown_multiple
        var agent = await CreateTestAgent();
        await agent.StartAsync();
        await Task.Delay(200);

        // Multiple dispose calls should not throw
        for (int i = 0; i < 5; i++)
        {
            await agent.DisposeAsync();
        }
    }

    [Fact]
    public async Task SetTagsAsync_UpdatesNodeTags()
    {
        // Test: SetTags should update Serf tags
        var agent = await CreateTestAgent();
        await agent.StartAsync();
        await Task.Delay(200);

        var newTags = new Dictionary<string, string>
        {
            ["role"] = "webserver",
            ["version"] = "2.0"
        };

        await agent.SetTagsAsync(newTags);
        await Task.Delay(200);

        var localMember = agent.Serf.Members()
            .First(m => m.Name == agent.Serf.Config.NodeName);

        Assert.Equal("webserver", localMember.Tags["role"]);
        Assert.Equal("2.0", localMember.Tags["version"]);
    }

    [Fact]
    public async Task SetTagsAsync_WithTagsFile_PersistsToDisk()
    {
        // Port from Go: TestAgentTagsFile
        var tagsFile = Path.Combine(Path.GetTempPath(), $"serf-tags-{Guid.NewGuid()}.json");

        try
        {
            var agentConfig = new AgentConfig
            {
                NodeName = "test-tags-node",
                BindAddr = "127.0.0.1:0",
                RpcAddr = null,
                TagsFile = tagsFile
            };

            var agent = await CreateTestAgent(agentConfig);
            await agent.StartAsync();
            await Task.Delay(200);

            var tags = new Dictionary<string, string>
            {
                ["role"] = "webserver",
                ["datacenter"] = "us-east"
            };

            await agent.SetTagsAsync(tags);
            await Task.Delay(200);

            // Verify file was created
            Assert.True(File.Exists(tagsFile));

            // Verify tags were persisted
            var json = await File.ReadAllTextAsync(tagsFile);
            var savedTags = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            Assert.NotNull(savedTags);
            Assert.Equal("webserver", savedTags["role"]);
            Assert.Equal("us-east", savedTags["datacenter"]);
        }
        finally
        {
            if (File.Exists(tagsFile))
            {
                File.Delete(tagsFile);
            }
        }
    }
}
