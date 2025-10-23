// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using System.Text.Json;
using Xunit;

namespace NSerfTests.Agent;

public class AgentTagsTests
{
    [Fact]
    public async Task Agent_LoadTagsFile_OnCreate()
    {
        var tagsFile = Path.GetTempFileName();
        var tags = new Dictionary<string, string>
        {
            ["env"] = "staging",
            ["region"] = "us-east"
        };
        await File.WriteAllTextAsync(tagsFile, JsonSerializer.Serialize(tags));

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

            // Tags loaded into config
            Assert.Equal("staging", config.Tags["env"]);
            Assert.Equal("us-east", config.Tags["region"]);

            await agent.DisposeAsync();
        }
        finally
        {
            if (File.Exists(tagsFile))
                File.Delete(tagsFile);
        }
    }

    [Fact]
    public async Task Agent_SaveTagsFile_OnUpdate()
    {
        var tagsFile = Path.GetTempFileName();

        try
        {
            var config = new AgentConfig
            {
                NodeName = "test-save-tags",
                BindAddr = "127.0.0.1:0",
                TagsFile = tagsFile
            };

            var agent = new SerfAgent(config);
            await agent.StartAsync();

            var newTags = new Dictionary<string, string>
            {
                ["role"] = "worker",
                ["zone"] = "az1"
            };

            await agent.SetTagsAsync(newTags);

            // Verify file was written
            var fileContent = await File.ReadAllTextAsync(tagsFile);
            var loadedTags = JsonSerializer.Deserialize<Dictionary<string, string>>(fileContent);

            Assert.NotNull(loadedTags);
            Assert.Equal("worker", loadedTags["role"]);
            Assert.Equal("az1", loadedTags["zone"]);

            await agent.DisposeAsync();
        }
        finally
        {
            if (File.Exists(tagsFile))
                File.Delete(tagsFile);
        }
    }

    [Fact]
    public async Task Agent_UpdateTags_AddsNewTags()
    {
        var config = new AgentConfig
        {
            NodeName = "test-add-tags",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        var initialTags = new Dictionary<string, string>
        {
            ["env"] = "dev"
        };
        await agent.SetTagsAsync(initialTags);

        var updatedTags = new Dictionary<string, string>
        {
            ["env"] = "dev",
            ["version"] = "3.0"  // New tag
        };
        await agent.SetTagsAsync(updatedTags);

        var localMember = agent.Serf!.LocalMember();
        Assert.Equal("dev", localMember.Tags["env"]);
        Assert.Equal("3.0", localMember.Tags["version"]);

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_UpdateTags_DeletesTags()
    {
        var config = new AgentConfig
        {
            NodeName = "test-delete-tags",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        var initialTags = new Dictionary<string, string>
        {
            ["env"] = "test",
            ["version"] = "1.0"
        };
        await agent.SetTagsAsync(initialTags);

        // Remove version tag
        var updatedTags = new Dictionary<string, string>
        {
            ["env"] = "test"
            // version removed
        };
        await agent.SetTagsAsync(updatedTags);

        var localMember = agent.Serf!.LocalMember();
        Assert.Equal("test", localMember.Tags["env"]);
        Assert.False(localMember.Tags.ContainsKey("version"));

        await agent.DisposeAsync();
    }

    [Fact]
    public void Agent_RoleTag_SpecialHandling()
    {
        var config = new AgentConfig
        {
            NodeName = "test-role",
            BindAddr = "127.0.0.1:0",
            Role = "api-server"  // Deprecated field
        };

        // Role should be in tags after merge
        var merged = AgentConfig.Merge(AgentConfig.Default(), config);
        Assert.Equal("api-server", merged.Tags["role"]);
    }
}
