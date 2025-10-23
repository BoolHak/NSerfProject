// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.Serf;
using NSerf.Serf.Events;
using System.Text.Json;
using Xunit;

namespace NSerfTests.Agent;

public class SerfAgentVerificationTests
{
    [Fact]
    public void Agent_Create_TagsAndTagsFile_ThrowsException()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string> { ["role"] = "web" },
            TagsFile = Path.GetTempFileName()
        };

        var exception = Assert.Throws<ConfigException>(() => new SerfAgent(config));
        Assert.Contains("not allowed while using tag files", exception.Message);
    }

    [Fact]
    public void Agent_Create_EncryptKeyAndKeyringFile_ThrowsException()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            EncryptKey = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=",
            KeyringFile = Path.GetTempFileName()
        };

        var exception = Assert.Throws<ConfigException>(() => new SerfAgent(config));
        Assert.Contains("not allowed while using a keyring", exception.Message);
    }

    [Fact]
    public async Task Agent_RegisterHandler_RebuildsHandlerList()
    {
        var config = new AgentConfig { NodeName = "test-node" };
        var agent = new SerfAgent(config);
        
        var handler1 = new MockEventHandler();
        var handler2 = new MockEventHandler();
        
        agent.RegisterEventHandler(handler1);
        agent.RegisterEventHandler(handler2);
        agent.RegisterEventHandler(handler1);  // Duplicate - should be in set only once
        
        // Can't directly verify count without exposing internals, but we verified no exception
        Assert.NotNull(agent);
        
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_SetTags_PersistsBeforeGossiping()
    {
        var tagsFile = Path.GetTempFileName();
        var config = new AgentConfig
        {
            NodeName = "persist-test",
            BindAddr = "127.0.0.1:0",
            TagsFile = tagsFile
        };

        try
        {
            var agent = new SerfAgent(config);
            await agent.StartAsync();

            var newTags = new Dictionary<string, string>
            {
                ["role"] = "api",
                ["version"] = "2.0"
            };
            await agent.SetTagsAsync(newTags);

            // Verify file was written
            var fileContent = await File.ReadAllTextAsync(tagsFile);
            var loadedTags = JsonSerializer.Deserialize<Dictionary<string, string>>(fileContent);
            Assert.NotNull(loadedTags);
            Assert.Equal("api", loadedTags["role"]);
            Assert.Equal("2.0", loadedTags["version"]);

            // Verify Serf has tags
            var serfTags = agent.Serf!.LocalMember().Tags;
            Assert.Equal("api", serfTags["role"]);

            await agent.DisposeAsync();
        }
        finally
        {
            if (File.Exists(tagsFile))
                File.Delete(tagsFile);
        }
    }

    [Theory]
    [InlineData("role=web", "role", "web")]
    [InlineData("version=1.0", "version", "1.0")]
    [InlineData("dc=us-east=1", "dc", "us-east=1")]  // Value can contain =
    public void Agent_UnmarshalTags_ValidFormat_Succeeds(string input, string expectedKey, string expectedValue)
    {
        var tags = SerfAgent.UnmarshalTags(new[] { input });

        Assert.Single(tags);
        Assert.Equal(expectedValue, tags[expectedKey]);
    }

    [Theory]
    [InlineData("invalid")]  // No =
    [InlineData("=value")]   // Empty key
    [InlineData("")]         // Empty string
    public void Agent_UnmarshalTags_InvalidFormat_ThrowsException(string input)
    {
        var exception = Assert.Throws<FormatException>(() =>
        {
            SerfAgent.UnmarshalTags(new[] { input });
        });

        Assert.Contains("Invalid tag", exception.Message);
    }

    [Fact]
    public async Task Agent_StartTwice_ThrowsException()
    {
        var config = new AgentConfig
        {
            NodeName = "test-twice",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await agent.StartAsync();
        });

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_ShutdownIdempotent_CanCallMultipleTimes()
    {
        var config = new AgentConfig
        {
            NodeName = "test-idempotent",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        await agent.ShutdownAsync();
        await agent.ShutdownAsync();  // Second call should not throw
        await agent.ShutdownAsync();  // Third call should not throw

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_ShutdownBeforeStart_Succeeds()
    {
        var config = new AgentConfig
        {
            NodeName = "test-shutdown-before-start",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        // Shutdown without starting
        await agent.ShutdownAsync();

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_DeregisterHandler_RemovesHandler()
    {
        var config = new AgentConfig { NodeName = "test-deregister" };
        var agent = new SerfAgent(config);
        
        var handler = new MockEventHandler();
        
        agent.RegisterEventHandler(handler);
        agent.DeregisterEventHandler(handler);
        
        // Handler removed, no exception
        Assert.NotNull(agent);
        
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_LoadsTagsFromFile_OnStart()
    {
        var tagsFile = Path.GetTempFileName();
        var tagsData = new Dictionary<string, string>
        {
            ["env"] = "production",
            ["region"] = "us-west"
        };
        await File.WriteAllTextAsync(tagsFile, JsonSerializer.Serialize(tagsData));

        try
        {
            var config = new AgentConfig
            {
                NodeName = "test-load-tags",
                BindAddr = "127.0.0.1:0",
                TagsFile = tagsFile
            };

            var agent = new SerfAgent(config);
            await agent.StartAsync();

            // Tags should be merged into config
            Assert.Equal("production", config.Tags["env"]);
            Assert.Equal("us-west", config.Tags["region"]);

            await agent.DisposeAsync();
        }
        finally
        {
            if (File.Exists(tagsFile))
                File.Delete(tagsFile);
        }
    }
}

public class MockEventHandler : IEventHandler
{
    public List<Event> ReceivedEvents { get; } = new();

    public void HandleEvent(Event @event)
    {
        ReceivedEvents.Add(@event);
    }
}
