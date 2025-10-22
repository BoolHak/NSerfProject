using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

/// <summary>
/// RED Phase: Configuration tests ported from Go's config_test.go
/// These tests will FAIL until we implement AgentConfig.cs
/// </summary>
public class AgentConfigTests
{
    [Fact]
    public void DefaultConfig_HasCorrectDefaults()
    {
        // Port from Go: TestDefaultConfig pattern
        var config = AgentConfig.Default();
        
        Assert.NotNull(config);
        Assert.NotNull(config.Tags);
        Assert.Empty(config.Tags);
        Assert.Equal("0.0.0.0:7946", config.BindAddr);
        Assert.Equal("127.0.0.1:7373", config.RpcAddr);
        Assert.Equal("INFO", config.LogLevel);
        Assert.False(config.DisableCoordinates);
        Assert.Equal("lan", config.Profile);
        Assert.Equal(TimeSpan.FromSeconds(30), config.RetryInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), config.BroadcastTimeout);
    }
    
    [Fact]
    public void Tags_CanBeSetAndRetrieved()
    {
        // Port from Go: DecodeConfig tags test
        var config = new AgentConfig
        {
            Tags = new Dictionary<string, string>
            {
                ["foo"] = "bar",
                ["role"] = "test"
            }
        };
        
        Assert.Equal("bar", config.Tags["foo"]);
        Assert.Equal("test", config.Tags["role"]);
    }
    
    [Fact]
    public void TagsFile_CanBeSet()
    {
        // Port from Go: DecodeConfig tags_file test
        var config = new AgentConfig
        {
            TagsFile = "/some/path/tags.json"
        };
        
        Assert.Equal("/some/path/tags.json", config.TagsFile);
    }
    
    [Fact]
    public void KeyringFile_CanBeSet()
    {
        // Port from Go: KeyringFile configuration
        var config = new AgentConfig
        {
            KeyringFile = "/etc/serf/keyring.json"
        };
        
        Assert.Equal("/etc/serf/keyring.json", config.KeyringFile);
    }
    
    [Fact]
    public void BindAddr_ParsesCorrectly()
    {
        // Port from Go: TestConfigBindAddrParts
        var testCases = new[]
        {
            new { Value = "0.0.0.0", IP = "0.0.0.0", Port = 7946 },
            new { Value = "0.0.0.0:1234", IP = "0.0.0.0", Port = 1234 },
            new { Value = "192.168.1.5", IP = "192.168.1.5", Port = 7946 },
            new { Value = "192.168.1.5:8080", IP = "192.168.1.5", Port = 8080 }
        };
        
        foreach (var tc in testCases)
        {
            var config = new AgentConfig { BindAddr = tc.Value };
            var (ip, port) = config.ParseBindAddr();
            
            Assert.Equal(tc.IP, ip);
            Assert.Equal(tc.Port, port);
        }
    }
    
    [Fact]
    public void RpcAddr_DefaultsCorrectly()
    {
        // Port from Go: RPC address configuration
        var config = AgentConfig.Default();
        
        Assert.Equal("127.0.0.1:7373", config.RpcAddr);
    }
    
    [Fact]
    public void RpcAuthKey_CanBeSet()
    {
        // Port from Go: DecodeConfig rpc_auth test
        var config = new AgentConfig
        {
            RpcAuthKey = "secret-auth-key"
        };
        
        Assert.Equal("secret-auth-key", config.RpcAuthKey);
    }
    
    [Fact]
    public void EncryptKey_CanBeDecoded()
    {
        // Port from Go: TestConfigEncryptBytes
        var plainKey = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };
        var base64Key = Convert.ToBase64String(plainKey);
        
        var config = new AgentConfig
        {
            EncryptKey = base64Key
        };
        
        var decodedKey = config.DecodeEncryptKey();
        
        Assert.Equal(plainKey, decodedKey);
    }
    
    [Fact]
    public void EncryptKey_EmptyReturnsEmpty()
    {
        // Port from Go: TestConfigEncryptBytes (no input test)
        var config = new AgentConfig();
        
        var result = config.DecodeEncryptKey();
        
        Assert.Empty(result);
    }
    
    [Fact]
    public void LogLevel_CanBeSet()
    {
        // Port from Go: LogLevel configuration
        var config = new AgentConfig
        {
            LogLevel = "DEBUG"
        };
        
        Assert.Equal("DEBUG", config.LogLevel);
    }
    
    [Fact]
    public void Profile_CanBeSet()
    {
        // Port from Go: Profile configuration
        var testCases = new[] { "lan", "wan", "local" };
        
        foreach (var profile in testCases)
        {
            var config = new AgentConfig { Profile = profile };
            Assert.Equal(profile, config.Profile);
        }
    }
    
    [Fact]
    public void EventHandlers_CanBeSet()
    {
        // Port from Go: DecodeConfig event_handlers test
        var config = new AgentConfig
        {
            EventHandlers = new[] { "script1.sh", "script2.sh" }
        };
        
        Assert.Equal(2, config.EventHandlers.Length);
        Assert.Equal("script1.sh", config.EventHandlers[0]);
        Assert.Equal("script2.sh", config.EventHandlers[1]);
    }
    
    [Fact]
    public void ReplayOnJoin_CanBeSet()
    {
        // Port from Go: DecodeConfig replay_on_join test
        var config = new AgentConfig
        {
            ReplayOnJoin = true
        };
        
        Assert.True(config.ReplayOnJoin);
    }
    
    [Fact]
    public void SnapshotPath_CanBeSet()
    {
        // Port from Go: SnapshotPath configuration
        var config = new AgentConfig
        {
            SnapshotPath = "/var/lib/serf/snapshot"
        };
        
        Assert.Equal("/var/lib/serf/snapshot", config.SnapshotPath);
    }
    
    [Fact]
    public void RetryJoin_CanBeSet()
    {
        // Port from Go: DecodeConfig retry_join test
        var config = new AgentConfig
        {
            RetryJoin = new[] { "127.0.0.1", "127.0.0.2" }
        };
        
        Assert.Equal(2, config.RetryJoin.Length);
        Assert.Equal("127.0.0.1", config.RetryJoin[0]);
        Assert.Equal("127.0.0.2", config.RetryJoin[1]);
    }
    
    [Fact]
    public void RetryInterval_CanBeSet()
    {
        // Port from Go: DecodeConfig retry_interval test
        var config = new AgentConfig
        {
            RetryInterval = TimeSpan.FromSeconds(60)
        };
        
        Assert.Equal(TimeSpan.FromSeconds(60), config.RetryInterval);
    }
    
    [Fact]
    public void RetryMaxAttempts_CanBeSet()
    {
        // Port from Go: DecodeConfig retry_max_attempts test
        var config = new AgentConfig
        {
            RetryMaxAttempts = 10
        };
        
        Assert.Equal(10, config.RetryMaxAttempts);
    }
    
    [Fact]
    public void RejoinAfterLeave_CanBeSet()
    {
        // Port from Go: DecodeConfig rejoin_after_leave test
        var config = new AgentConfig
        {
            RejoinAfterLeave = true
        };
        
        Assert.True(config.RejoinAfterLeave);
    }
    
    [Fact]
    public void EnableCompression_DefaultsToTrue()
    {
        // Port from Go: EnableCompression configuration
        var config = AgentConfig.Default();
        
        Assert.True(config.EnableCompression);
    }
    
    [Fact]
    public void DisableCoordinates_DefaultsToFalse()
    {
        // Port from Go: DisableCoordinates configuration
        var config = AgentConfig.Default();
        
        Assert.False(config.DisableCoordinates);
    }
    
    [Fact]
    public void QuerySizeLimits_CanBeSet()
    {
        // Port from Go: DecodeConfig query size limits test
        var config = new AgentConfig
        {
            QuerySizeLimit = 2048,
            QueryResponseSizeLimit = 4096
        };
        
        Assert.Equal(2048, config.QuerySizeLimit);
        Assert.Equal(4096, config.QueryResponseSizeLimit);
    }
}
