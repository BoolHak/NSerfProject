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
            "cg8StVXbQJ0gPvMd9pJItg=="
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

            var agent = new SerfAgent(config);
            await agent.StartAsync();

            // Verify keyring was loaded
            var keyring = agent.Serf!.Config.MemberlistConfig?.Keyring;
            Assert.NotNull(keyring);

            // Verify keys were loaded
            var loadedKeys = keyring.GetKeys();
            Assert.NotEmpty(loadedKeys);
            var loadedKeysBase64 = loadedKeys.Select(k => Convert.ToBase64String(k)).ToArray();
            Assert.Contains(keys[0], loadedKeysBase64);

            await agent.DisposeAsync();
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

        // Start with one key
        var initialKeys = new[] { "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=" };
        await File.WriteAllTextAsync(keyringFile, JsonSerializer.Serialize(initialKeys));

        try
        {
            var config = new AgentConfig
            {
                NodeName = "test-install-key",
                BindAddr = "127.0.0.1:0",
                KeyringFile = keyringFile
            };

            var agent = new SerfAgent(config);
            await agent.StartAsync();

            var keyring = agent.Serf!.Config.MemberlistConfig?.Keyring;
            Assert.NotNull(keyring);
            var keysBefore = keyring.GetKeys().Count;

            // Install a new key
            var newKey = "cg8StVXbQJ0gPvMd9pJItg==";
            var newKeyBytes = Convert.FromBase64String(newKey);
            keyring.AddKey(newKeyBytes);
            await agent.Serf.WriteKeyringFileAsync();

            // Verify key was added
            var keysAfter = keyring.GetKeys();
            Assert.Equal(keysBefore + 1, keysAfter.Count);
            var keysAfterBase64 = keysAfter.Select(k => Convert.ToBase64String(k)).ToArray();
            Assert.Contains(newKey, keysAfterBase64);

            // Verify keyring file was updated
            var fileContent = await File.ReadAllTextAsync(keyringFile);
            var savedKeys = JsonSerializer.Deserialize<string[]>(fileContent);
            Assert.Contains(newKey, savedKeys!);

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
        var keyringFile = Path.GetTempFileName();

        // Start with multiple keys
        var initialKeys = new[]
        {
            "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=",
            "cg8StVXbQJ0gPvMd9pJItg=="
        };
        await File.WriteAllTextAsync(keyringFile, JsonSerializer.Serialize(initialKeys));

        try
        {
            var config = new AgentConfig
            {
                NodeName = "test-remove-key",
                BindAddr = "127.0.0.1:0",
                KeyringFile = keyringFile
            };

            var agent = new SerfAgent(config);
            await agent.StartAsync();

            var keyring = agent.Serf!.Config.MemberlistConfig?.Keyring;
            Assert.NotNull(keyring);
            var keysBefore = keyring.GetKeys().Count;

            // Remove a non-primary key
            var keyToRemove = initialKeys[1];
            var keyToRemoveBytes = Convert.FromBase64String(keyToRemove);
            keyring.RemoveKey(keyToRemoveBytes);
            await agent.Serf.WriteKeyringFileAsync();

            // Verify key was removed
            var keysAfter = keyring.GetKeys();
            Assert.Equal(keysBefore - 1, keysAfter.Count);
            var keysAfterBase64 = keysAfter.Select(k => Convert.ToBase64String(k)).ToArray();
            Assert.DoesNotContain(keyToRemove, keysAfterBase64);

            // Verify keyring file was updated
            var fileContent = await File.ReadAllTextAsync(keyringFile);
            var savedKeys = JsonSerializer.Deserialize<string[]>(fileContent);
            Assert.DoesNotContain(keyToRemove, savedKeys!);

            await agent.DisposeAsync();
        }
        finally
        {
            if (File.Exists(keyringFile))
                File.Delete(keyringFile);
        }
    }

    [Fact]
    public async Task Agent_ListKeys_ReturnsAllKeys()
    {
        var keyringFile = Path.GetTempFileName();

        var expectedKeys = new[]
        {
            "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=",  // 32 bytes
            "cg8StVXbQJ0gPvMd9pJItg==",                      // 16 bytes
            "3nPSUrXwbDi2dhbtqir37sT9jncgl9mbLus+baTTa7o="   // 32 bytes (fixed)
        };
        await File.WriteAllTextAsync(keyringFile, JsonSerializer.Serialize(expectedKeys));

        try
        {
            var config = new AgentConfig
            {
                NodeName = "test-list-keys",
                BindAddr = "127.0.0.1:0",
                KeyringFile = keyringFile
            };

            var agent = new SerfAgent(config);
            await agent.StartAsync();

            // List all keys via Keyring
            var keyring = agent.Serf!.Config.MemberlistConfig?.Keyring;
            Assert.NotNull(keyring);
            var keys = keyring.GetKeys();
            var keysBase64 = keys.Select(k => Convert.ToBase64String(k)).ToArray();

            // Verify all keys are returned
            Assert.Equal(expectedKeys.Length, keysBase64.Length);
            foreach (var expectedKey in expectedKeys)
            {
                Assert.Contains(expectedKey, keysBase64);
            }

            await agent.DisposeAsync();
        }
        finally
        {
            if (File.Exists(keyringFile))
                File.Delete(keyringFile);
        }
    }
}
