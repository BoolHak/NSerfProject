# Phase 4: Agent Configuration - Detailed Test Specification

**Timeline:** Week 4  
**Test Count:** 33 tests  
**Focus:** Configuration parsing, validation, tags/keyring persistence

**Prerequisites:** Phases 1-3 complete (RPC client fully functional)

---

## Implementation Summary

### Files to Create
```
NSerf/NSerf/Agent/
├── AgentConfig.cs (~300 lines)
├── SerfConfig.cs (~150 lines)
├── EventScript.cs (~100 lines)
├── EventFilter.cs (~80 lines)
├── ConfigValidator.cs (~200 lines)
└── ConfigLoader.cs (~150 lines)

NSerfTests/Agent/
├── AgentConfigTests.cs (3 tests)
├── ConfigLoadTests.cs (8 tests)
├── ConfigValidationTests.cs (8 tests)
├── TagsPersistenceTests.cs (5 tests)
├── EventScriptTests.cs (5 tests)
└── KeyringTests.cs (4 tests)
```

---

## Test Group 4.1: Default Configuration (3 tests)

### Test 4.1.1: Default Config Has Correct Defaults

```csharp
[Fact]
public void AgentConfig_Default_HasCorrectDefaults()
{
    // Act
    var config = AgentConfig.Default();

    // Assert
    Assert.Equal("0.0.0.0", config.BindAddr);
    Assert.Equal("127.0.0.1:7373", config.RpcAddr);
    Assert.Equal("INFO", config.LogLevel);
    Assert.Equal("lan", config.Profile);
    Assert.Equal(7946, config.DefaultBindPort);
    Assert.False(config.DisableCoordinates);
    Assert.NotNull(config.Tags);
    Assert.Empty(config.Tags);
}
```

---

## Test Group 4.2: JSON Configuration Loading (8 tests)

### Test 4.2.1: Load From JSON Parses All Fields

```csharp
[Fact]
public async Task AgentConfig_LoadFromJson_ParsesAllFields()
{
    // Arrange
    var json = @"{
        ""node_name"": ""test-node"",
        ""bind"": ""192.168.1.10:7946"",
        ""rpc_addr"": ""127.0.0.1:7373"",
        ""encrypt_key"": ""cg8StVXbQJ0gPvMd9pJItg=="",
        ""log_level"": ""DEBUG"",
        ""protocol"": 5,
        ""tags"": {
            ""role"": ""web"",
            ""datacenter"": ""us-east""
        },
        ""event_handlers"": [
            ""member-join=/usr/local/bin/join.sh"",
            ""user:deploy=/usr/local/bin/deploy.sh""
        ]
    }";
    var tempFile = Path.GetTempFileName();
    await File.WriteAllTextAsync(tempFile, json);

    try
    {
        // Act
        var config = await ConfigLoader.LoadFromFileAsync(tempFile);

        // Assert
        Assert.Equal("test-node", config.NodeName);
        Assert.Equal("192.168.1.10:7946", config.BindAddr);
        Assert.Equal("127.0.0.1:7373", config.RpcAddr);
        Assert.Equal("DEBUG", config.LogLevel);
        Assert.Equal(5, config.Protocol);
        Assert.Equal(2, config.Tags.Count);
        Assert.Equal("web", config.Tags["role"]);
        Assert.Equal(2, config.EventHandlers.Count);
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

### Test 4.2.2: Load Handles Optional Fields

```csharp
[Fact]
public async Task AgentConfig_LoadFromJson_HandlesOptionalFields()
{
    // Arrange - Minimal config
    var json = @"{ ""node_name"": ""minimal"" }";
    var tempFile = Path.GetTempFileName();
    await File.WriteAllTextAsync(tempFile, json);

    try
    {
        // Act
        var config = await ConfigLoader.LoadFromFileAsync(tempFile);

        // Assert
        Assert.Equal("minimal", config.NodeName);
        Assert.Equal(AgentConfig.Default().BindAddr, config.BindAddr);
        Assert.Equal(AgentConfig.Default().LogLevel, config.LogLevel);
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

### Test 4.2.3: Merge Configs - CLI Overrides File

```csharp
[Fact]
public void AgentConfig_Merge_CommandLineOverridesFile()
{
    // Arrange
    var fileConfig = new AgentConfig
    {
        NodeName = "from-file",
        LogLevel = "INFO",
        BindAddr = "0.0.0.0:7946"
    };

    var cliConfig = new AgentConfig
    {
        NodeName = "from-cli", // Override
        LogLevel = "DEBUG"      // Override
        // BindAddr not set - should keep file value
    };

    // Act
    var merged = AgentConfig.Merge(fileConfig, cliConfig);

    // Assert
    Assert.Equal("from-cli", merged.NodeName);
    Assert.Equal("DEBUG", merged.LogLevel);
    Assert.Equal("0.0.0.0:7946", merged.BindAddr);
}
```

---

## Test Group 4.3: Tags File Persistence (5 tests)

### Test 4.3.1: Tags File Loads Existing Tags

```csharp
[Fact]
public async Task AgentConfig_TagsFile_LoadsExistingTags()
{
    // Arrange
    var tagsFile = Path.GetTempFileName();
    var tagsJson = @"{ ""role"": ""web"", ""version"": ""1.0"" }";
    await File.WriteAllTextAsync(tagsFile, tagsJson);

    try
    {
        var config = new AgentConfig { TagsFile = tagsFile };

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
```

### Test 4.3.2: Tags File Saves Updated Tags

```csharp
[Fact]
public async Task AgentConfig_TagsFile_SavesUpdatedTags()
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
```

### Test 4.3.3: Tags File Not Found Creates New

```csharp
[Fact]
public async Task AgentConfig_TagsFile_NotFound_CreatesNew()
{
    // Arrange
    var tagsFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
    var tags = new Dictionary<string, string> { ["new"] = "tag" };

    try
    {
        // Act
        await ConfigLoader.SaveTagsToFileAsync(tagsFile, tags);

        // Assert
        Assert.True(File.Exists(tagsFile));
        var loaded = await ConfigLoader.LoadTagsFromFileAsync(tagsFile);
        Assert.Single(loaded);
    }
    finally
    {
        if (File.Exists(tagsFile))
            File.Delete(tagsFile);
    }
}
```

---

## Test Group 4.4: Keyring File Loading (4 tests)

### Test 4.4.1: Keyring File Loads Keys

```csharp
[Fact]
public async Task AgentConfig_KeyringFile_LoadsKeys()
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
```

---

## Test Group 4.5: Configuration Validation (8 tests)

### Test 4.5.1: Validate Valid Config Succeeds

```csharp
[Fact]
public void AgentConfig_Validate_ValidConfig_Succeeds()
{
    // Arrange
    var config = AgentConfig.Default();
    config.NodeName = "valid-node";

    // Act
    var result = ConfigValidator.Validate(config);

    // Assert
    Assert.True(result.IsValid);
    Assert.Empty(result.Errors);
}
```

### Test 4.5.2: Validate Invalid BindAddr Fails

```csharp
[Fact]
public void AgentConfig_Validate_InvalidBindAddr_Fails()
{
    // Arrange
    var config = AgentConfig.Default();
    config.BindAddr = "not-a-valid-address";

    // Act
    var result = ConfigValidator.Validate(config);

    // Assert
    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.Contains("bind", StringComparison.OrdinalIgnoreCase));
}
```

### Test 4.5.3: Validate Invalid EncryptKey Fails

```csharp
[Fact]
public void AgentConfig_Validate_InvalidEncryptKey_Fails()
{
    // Arrange
    var config = AgentConfig.Default();
    config.EncryptKey = "not-32-bytes"; // Invalid

    // Act
    var result = ConfigValidator.Validate(config);

    // Assert
    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.Contains("encrypt", StringComparison.OrdinalIgnoreCase));
}
```

### Test 4.5.4: Validate Invalid LogLevel Fails

```csharp
[Fact]
public void AgentConfig_Validate_InvalidLogLevel_Fails()
{
    // Arrange
    var config = AgentConfig.Default();
    config.LogLevel = "INVALID";

    // Act
    var result = ConfigValidator.Validate(config);

    // Assert
    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.Contains("log level", StringComparison.OrdinalIgnoreCase));
}
```

### Test 4.5.5: Validate Invalid Profile Fails

```csharp
[Fact]
public void AgentConfig_Validate_InvalidProfile_Fails()
{
    // Arrange
    var config = AgentConfig.Default();
    config.Profile = "invalid"; // Must be "lan", "wan", or "local"

    // Act
    var result = ConfigValidator.Validate(config);

    // Assert
    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.Contains("profile", StringComparison.OrdinalIgnoreCase));
}
```

---

## Test Group 4.6: Event Scripts Parsing (5 tests)

### Test 4.6.1: Parse Simple Script (All Events)

```csharp
[Fact]
public void EventScript_Parse_SimpleScript_AllEvents()
{
    // Arrange
    var scriptString = "/usr/local/bin/handler.sh";

    // Act
    var scripts = EventScript.Parse(scriptString);

    // Assert
    Assert.Single(scripts);
    Assert.Equal("*", scripts[0].Filter.Event);
    Assert.Equal("/usr/local/bin/handler.sh", scripts[0].Script);
}
```

### Test 4.6.2: Parse With Event Filter

```csharp
[Fact]
public void EventScript_Parse_WithFilter()
{
    // Arrange
    var scriptString = "member-join=/usr/local/bin/join.sh";

    // Act
    var scripts = EventScript.Parse(scriptString);

    // Assert
    Assert.Single(scripts);
    Assert.Equal("member-join", scripts[0].Filter.Event);
    Assert.Equal("/usr/local/bin/join.sh", scripts[0].Script);
}
```

### Test 4.6.3: Parse User Event Filter

```csharp
[Fact]
public void EventScript_Parse_UserEventFilter()
{
    // Arrange
    var scriptString = "user:deploy=/usr/local/bin/deploy.sh";

    // Act
    var scripts = EventScript.Parse(scriptString);

    // Assert
    Assert.Single(scripts);
    Assert.Equal("user", scripts[0].Filter.Event);
    Assert.Equal("deploy", scripts[0].Filter.Name);
    Assert.Equal("/usr/local/bin/deploy.sh", scripts[0].Script);
}
```

### Test 4.6.4: Parse Query Filter

```csharp
[Fact]
public void EventScript_Parse_QueryFilter()
{
    // Arrange
    var scriptString = "query:backup=/usr/local/bin/backup.sh";

    // Act
    var scripts = EventScript.Parse(scriptString);

    // Assert
    Assert.Single(scripts);
    Assert.Equal("query", scripts[0].Filter.Event);
    Assert.Equal("backup", scripts[0].Filter.Name);
}
```

### Test 4.6.5: Parse Invalid Filter Returns Error

```csharp
[Fact]
public void EventScript_Parse_InvalidFilter_ThrowsException()
{
    // Arrange
    var scriptString = "invalid-event=/usr/local/bin/script.sh";

    // Act & Assert
    var exception = Assert.Throws<ArgumentException>(() => EventScript.Parse(scriptString));
    Assert.Contains("invalid event type", exception.Message, StringComparison.OrdinalIgnoreCase);
}
```

---

## Implementation Notes

### AgentConfig.cs Structure

```csharp
public class AgentConfig
{
    public string NodeName { get; set; }
    public string Role { get; set; } // Deprecated, use Tags["role"]
    public Dictionary<string, string> Tags { get; set; }
    public string TagsFile { get; set; }
    public string BindAddr { get; set; }
    public string AdvertiseAddr { get; set; }
    public string EncryptKey { get; set; }
    public string KeyringFile { get; set; }
    public string LogLevel { get; set; }
    public string RpcAddr { get; set; }
    public string RpcAuthKey { get; set; }
    public int Protocol { get; set; }
    public bool ReplayOnJoin { get; set; }
    public List<string> EventHandlers { get; set; }
    public string Profile { get; set; } // lan, wan, local
    public string SnapshotPath { get; set; }
    public bool DisableCoordinates { get; set; }
    
    public static AgentConfig Default() { ... }
    public static AgentConfig Merge(AgentConfig file, AgentConfig cli) { ... }
}
```

### ConfigValidator.cs

```csharp
public static class ConfigValidator
{
    public static ValidationResult Validate(AgentConfig config)
    {
        var errors = new List<string>();
        
        // Validate BindAddr
        if (!IsValidAddress(config.BindAddr))
            errors.Add($"Invalid bind address: {config.BindAddr}");
        
        // Validate EncryptKey (32 bytes base64)
        if (!string.IsNullOrEmpty(config.EncryptKey))
        {
            if (!IsValidEncryptKey(config.EncryptKey))
                errors.Add("Encrypt key must be 32 bytes, base64 encoded");
        }
        
        // Validate LogLevel
        var validLevels = new[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR" };
        if (!validLevels.Contains(config.LogLevel.ToUpper()))
            errors.Add($"Invalid log level: {config.LogLevel}");
        
        // Validate Profile
        var validProfiles = new[] { "lan", "wan", "local" };
        if (!validProfiles.Contains(config.Profile.ToLower()))
            errors.Add($"Invalid profile: {config.Profile}");
        
        return new ValidationResult(errors);
    }
}
```

---

## Acceptance Criteria

- [ ] All 33 tests passing
- [ ] JSON configuration parsing works
- [ ] Config validation comprehensive
- [ ] Tags persistence to/from file
- [ ] Keyring persistence
- [ ] Event script parsing
- [ ] Config merging (file + CLI)
- [ ] Code coverage >95%

---

## Go Reference

**Config:** `serf/cmd/serf/command/agent/config.go`  
**Tests:** `serf/cmd/serf/command/agent/config_test.go`  
**Event Scripts:** `serf/cmd/serf/command/agent/event_handler.go`

---

## Next Phase Preview

**Phase 5** implements the Agent core wrapper that manages Serf lifecycle, event handlers, and ties everything together.
