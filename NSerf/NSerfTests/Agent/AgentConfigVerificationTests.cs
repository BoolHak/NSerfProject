// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

/// <summary>
/// Tests from PHASE4_VERIFICATION_REPORT.md
/// These tests validate critical configuration behaviors from Go implementation
/// </summary>
public class AgentConfigVerificationTests
{
    [Fact]
    public async Task AgentConfig_Load_ParsesDurations()
    {
        // Arrange
        var json = @"{
            ""reconnect_interval"": ""15s"",
            ""reconnect_timeout"": ""48h"",
            ""tombstone_timeout"": ""24h"",
            ""retry_interval"": ""60s"",
            ""broadcast_timeout"": ""10s""
        }";
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, json);

        try
        {
            // Act
            var config = await ConfigLoader.LoadFromFileAsync(tempFile);

            // Assert
            Assert.Equal(TimeSpan.FromSeconds(15), config.ReconnectInterval);
            Assert.Equal(TimeSpan.FromHours(48), config.ReconnectTimeout);
            Assert.Equal(TimeSpan.FromHours(24), config.TombstoneTimeout);
            Assert.Equal(TimeSpan.FromSeconds(60), config.RetryInterval);
            Assert.Equal(TimeSpan.FromSeconds(10), config.BroadcastTimeout);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AgentConfig_LoadFromDirectory_MergesAllJsonFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create multiple config files
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "01-base.json"),
                @"{""node_name"": ""base"", ""log_level"": ""INFO""}");
            
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "02-override.json"),
                @"{""log_level"": ""DEBUG""}");  // Override
            
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "03-extra.txt"),
                @"{""node_name"": ""ignore""}");  // Not .json - ignore

            // Act
            var config = await ConfigLoader.LoadFromDirectoryAsync(tempDir);

            // Assert
            Assert.Equal("base", config.NodeName);  // From 01-base.json
            Assert.Equal("DEBUG", config.LogLevel);  // From 02-override.json (later = wins)
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AgentConfig_Load_UnknownDirective_ThrowsException()
    {
        // Arrange
        var json = @"{""unknown_directive"": ""titi"", ""node_name"": ""test""}";
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, json);

        try
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ConfigException>(async () =>
            {
                await ConfigLoader.LoadFromFileAsync(tempFile);
            });

            Assert.Contains("unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void AgentConfig_Merge_Arrays_AreAppended()
    {
        // Arrange
        var config1 = new AgentConfig
        {
            EventHandlers = new List<string> { "handler1.sh", "handler2.sh" },
            StartJoin = new[] { "node1:7946" }
        };

        var config2 = new AgentConfig
        {
            EventHandlers = new List<string> { "handler3.sh" },
            StartJoin = new[] { "node2:7946" }
        };

        // Act
        var merged = AgentConfig.Merge(config1, config2);

        // Assert - Arrays are APPENDED
        Assert.Equal(3, merged.EventHandlers.Count);
        Assert.Contains("handler1.sh", merged.EventHandlers);
        Assert.Contains("handler2.sh", merged.EventHandlers);
        Assert.Contains("handler3.sh", merged.EventHandlers);

        Assert.Equal(2, merged.StartJoin.Length);
        Assert.Contains("node1:7946", merged.StartJoin);
        Assert.Contains("node2:7946", merged.StartJoin);
    }

    [Fact]
    public void AgentConfig_Merge_Tags_AreMerged()
    {
        // Arrange
        var config1 = new AgentConfig
        {
            Tags = new Dictionary<string, string>
            {
                ["datacenter"] = "us-east",
                ["role"] = "web"
            }
        };

        var config2 = new AgentConfig
        {
            Tags = new Dictionary<string, string>
            {
                ["role"] = "api",  // Override
                ["version"] = "1.0"  // Add new
            }
        };

        // Act
        var merged = AgentConfig.Merge(config1, config2);

        // Assert
        Assert.Equal(3, merged.Tags.Count);
        Assert.Equal("us-east", merged.Tags["datacenter"]);  // From config1
        Assert.Equal("api", merged.Tags["role"]);  // Overridden by config2
        Assert.Equal("1.0", merged.Tags["version"]);  // From config2
    }

    [Fact]
    public void AgentConfig_Merge_BooleanZeroValues_HandledCorrectly()
    {
        // Arrange
        var config1 = new AgentConfig { RejoinAfterLeave = false };
        var config2 = new AgentConfig { RejoinAfterLeave = true };

        // Act
        var merged = AgentConfig.Merge(config1, config2);

        // Assert
        Assert.True(merged.RejoinAfterLeave);  // config2 wins (OR logic)

        // Test reverse - OR logic means if either is true, result is true
        var merged2 = AgentConfig.Merge(config2, config1);
        Assert.True(merged2.RejoinAfterLeave);  // Still true (OR logic)
    }

    [Fact]
    public async Task AgentConfig_Load_RoleField_MapsToTag()
    {
        // Arrange
        var json = @"{""role"": ""web"", ""tags"": {""datacenter"": ""us-east""}}";
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, json);

        try
        {
            // Act
            var config = await ConfigLoader.LoadFromFileAsync(tempFile);

            // Assert
            Assert.Equal("web", config.Role);  // Deprecated field
            Assert.Equal("us-east", config.Tags["datacenter"]);
            
            // Apply merge to ensure Role goes into Tags
            var merged = AgentConfig.Merge(AgentConfig.Default(), config);
            Assert.Equal("web", merged.Tags["role"]);  // Should be in tags
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AgentConfig_Load_AllFieldsSupported()
    {
        // Arrange - Kitchen sink config
        var json = @"{
            ""node_name"": ""test"",
            ""retry_join"": [""node1:7946""],
            ""retry_max_attempts"": 5,
            ""rejoin_after_leave"": true,
            ""leave_on_term"": true,
            ""advertise"": ""192.168.1.10:7946"",
            ""user_event_size_limit"": 2048
        }";
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, json);

        try
        {
            // Act
            var config = await ConfigLoader.LoadFromFileAsync(tempFile);

            // Assert
            Assert.Single(config.RetryJoin);
            Assert.Equal(5, config.RetryMaxAttempts);
            Assert.True(config.RejoinAfterLeave);
            Assert.True(config.LeaveOnTerm);
            Assert.Equal("192.168.1.10:7946", config.AdvertiseAddr);
            Assert.Equal(2048, config.UserEventSizeLimit);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void AgentConfig_EncryptBytes_ValidatesLength()
    {
        // Arrange - Valid 32-byte key (base64)
        var config = new AgentConfig
        {
            EncryptKey = "cg8StVXbQJ0gPvMd9pJItg==" // This is only 16 bytes, should fail
        };

        // Act & Assert
        Assert.Throws<ConfigException>(() => config.EncryptBytes());
    }

    [Fact]
    public void AgentConfig_EncryptBytes_Valid32Bytes()
    {
        // Arrange - Valid 32-byte key (44 chars base64 = 32 bytes)
        var config = new AgentConfig
        {
            EncryptKey = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s="
        };

        // Act
        var bytes = config.EncryptBytes();

        // Assert
        Assert.NotNull(bytes);
        Assert.Equal(32, bytes.Length);
    }

    [Fact]
    public void AgentConfig_AddrParts_ParsesCorrectly()
    {
        // Arrange
        var config = new AgentConfig();

        // Act & Assert
        var (ip1, port1) = config.AddrParts("127.0.0.1");
        Assert.Equal("127.0.0.1", ip1);
        Assert.Equal(7946, port1);

        var (ip2, port2) = config.AddrParts("127.0.0.1:8000");
        Assert.Equal("127.0.0.1", ip2);
        Assert.Equal(8000, port2);

        var (ip3, port3) = config.AddrParts(":8000");
        Assert.Equal("0.0.0.0", ip3);
        Assert.Equal(8000, port3);
    }

    [Fact]
    public async Task ConfigLoader_LoadTagsFromFile_LoadsCorrectly()
    {
        // Arrange
        var tagsFile = Path.GetTempFileName();
        var tagsJson = @"{""role"": ""web"", ""version"": ""1.0""}";
        await File.WriteAllTextAsync(tagsFile, tagsJson);

        try
        {
            // Act
            var tags = await ConfigLoader.LoadTagsFromFileAsync(tagsFile);

            // Assert
            Assert.Equal(2, tags.Count);
            Assert.Equal("web", tags["role"]);
            Assert.Equal("1.0", tags["version"]);
        }
        finally
        {
            File.Delete(tagsFile);
        }
    }

    [Fact]
    public async Task ConfigLoader_SaveTagsToFile_SavesCorrectly()
    {
        // Arrange
        var tagsFile = Path.GetTempFileName();
        var tags = new Dictionary<string, string>
        {
            ["role"] = "database",
            ["environment"] = "production"
        };

        try
        {
            // Act
            await ConfigLoader.SaveTagsToFileAsync(tagsFile, tags);

            // Assert
            var loaded = await ConfigLoader.LoadTagsFromFileAsync(tagsFile);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("database", loaded["role"]);
            Assert.Equal("production", loaded["environment"]);
        }
        finally
        {
            File.Delete(tagsFile);
        }
    }

    [Fact]
    public async Task ConfigLoader_LoadKeyringFromFile_LoadsKeys()
    {
        // Arrange
        var keyringFile = Path.GetTempFileName();
        var keys = new[] 
        { 
            "cg8StVXbQJ0gPvMd9pJItg==",
            "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s="
        };
        var keyringJson = System.Text.Json.JsonSerializer.Serialize(keys);
        await File.WriteAllTextAsync(keyringFile, keyringJson);

        try
        {
            // Act
            var loaded = await ConfigLoader.LoadKeyringFromFileAsync(keyringFile);

            // Assert
            Assert.Equal(2, loaded.Length);
            Assert.Contains("cg8StVXbQJ0gPvMd9pJItg==", loaded);
        }
        finally
        {
            File.Delete(keyringFile);
        }
    }
}
