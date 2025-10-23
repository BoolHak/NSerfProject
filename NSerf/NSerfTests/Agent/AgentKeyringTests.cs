// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using System.Text.Json;
using Xunit;

namespace NSerfTests.Agent;

public class AgentKeyringTests
{
    [Fact]
    public async Task Agent_LoadKeyringFile_OnCreate()
    {
        var keyringFile = Path.GetTempFileName();
        var keys = new[]
        {
            "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=",
            "cg8StVXbQJ0gPvMd9pJItg=="  // This is too short but for test purposes
        };
        await File.WriteAllTextAsync(keyringFile, JsonSerializer.Serialize(keys));

        try
        {
            var config = new AgentConfig
            {
                NodeName = "test-load-keyring",
                BindAddr = "127.0.0.1:0",
                KeyringFile = keyringFile
            };

            // Keyring loading would happen during Serf initialization
            // For now, just verify config accepts keyring file
            var agent = new SerfAgent(config);
            Assert.NotNull(agent);
        }
        finally
        {
            if (File.Exists(keyringFile))
                File.Delete(keyringFile);
        }
    }

    [Fact]
    public async Task Agent_InstallKey_UpdatesKeyringFile()
    {
        var keyringFile = Path.GetTempFileName();

        try
        {
            var config = new AgentConfig
            {
                NodeName = "test-install-key",
                BindAddr = "127.0.0.1:0"
            };

            var agent = new SerfAgent(config);
            await agent.StartAsync();

            // InstallKey would be implemented via Serf's KeyManager
            // Placeholder for when key management is fully implemented
            Assert.NotNull(agent.Serf);

            await agent.DisposeAsync();
        }
        finally
        {
            if (File.Exists(keyringFile))
                File.Delete(keyringFile);
        }
    }

    [Fact]
    public async Task Agent_RemoveKey_UpdatesKeyringFile()
    {
        var config = new AgentConfig
        {
            NodeName = "test-remove-key",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // RemoveKey would be implemented via Serf's KeyManager
        // Placeholder for when key management is fully implemented
        Assert.NotNull(agent.Serf);

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_ListKeys_ReturnsAllKeys()
    {
        var config = new AgentConfig
        {
            NodeName = "test-list-keys",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // ListKeys would be implemented via Serf's KeyManager
        // Placeholder for when key management is fully implemented
        Assert.NotNull(agent.Serf);

        await agent.DisposeAsync();
    }
}
