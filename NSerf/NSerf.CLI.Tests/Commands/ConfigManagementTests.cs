// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Unit")]
public class ConfigManagementTests
{
    [Fact]
    public void Config_StartJoin_ParsesCorrectly()
    {
        var config = new AgentConfig
        {
            StartJoin = new[] { "10.0.0.1", "10.0.0.2:8000" }
        };
        
        Assert.Equal(2, config.StartJoin.Length);
        Assert.Equal("10.0.0.1", config.StartJoin[0]);
        Assert.Equal("10.0.0.2:8000", config.StartJoin[1]);
    }

    [Fact]
    public void Config_Tags_MergeCorrectly()
    {
        var config = new AgentConfig
        {
            Tags = new Dictionary<string, string>
            {
                ["role"] = "web",
                ["datacenter"] = "us-west"
            }
        };
        
        Assert.Equal(2, config.Tags.Count);
        Assert.Equal("web", config.Tags["role"]);
        Assert.Equal("us-west", config.Tags["datacenter"]);
    }

    [Fact]
    public void Config_RetryInterval_DefaultValue()
    {
        var config = new AgentConfig();
        Assert.Equal(TimeSpan.FromSeconds(30), config.RetryInterval);
    }

    [Fact]
    public void Config_RetryMaxAttempts_DefaultZero()
    {
        var config = new AgentConfig();
        Assert.Equal(0, config.RetryMaxAttempts);
    }

    [Fact]
    public void Config_MutualExclusion_TagsAndTagsFile_Throws()
    {
        Assert.Throws<ConfigException>(() => new SerfAgent(new AgentConfig
        {
            NodeName = "test",
            Tags = new Dictionary<string, string> { ["key"] = "value" },
            TagsFile = "/tmp/tags.json"
        }));
    }

    [Fact]
    public void Config_MutualExclusion_EncryptKeyAndKeyringFile_Throws()
    {
        Assert.Throws<ConfigException>(() => new SerfAgent(new AgentConfig
        {
            NodeName = "test",
            EncryptKey = "dGVzdGtleTE2Ynl0ZXNrZXkxNmI=",
            KeyringFile = "/tmp/keyring"
        }));
    }

    [Fact]
    public void Config_Profile_DefaultLan()
    {
        var config = new AgentConfig();
        Assert.Equal("lan", config.Profile);
    }

    [Fact]
    public void Config_Protocol_DefaultFive()
    {
        var config = new AgentConfig();
        Assert.Equal(5, config.Protocol);
    }

    [Fact]
    public void Config_LeaveOnTerm_DefaultTrue()
    {
        var config = new AgentConfig();
        Assert.True(config.LeaveOnTerm);
    }
}
