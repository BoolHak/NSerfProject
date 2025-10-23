# Phase 7 Verification Report

**Date:** Based on Go code review and DeepWiki analysis  
**Reviewer:** Verified against `serf/cmd/serf/command/agent/event_handler.go` and `invoke.go`

---

## Summary of Findings

### ✅ Tests to Add: 10
### ⚠️ Critical Implementation Details: 14
### 📊 Test Coverage: 25 → 35 tests (+40%)

---

## Missing Tests Discovered

### 1. UpdateScripts Hot-Reload Mechanism ⚠️ NEW

**File:** `event_handler.go` lines 31-38, 60-64  
**Severity:** 🔴 CRITICAL

```go
func (h *ScriptEventHandler) HandleEvent(e serf.Event) {
    // Swap in the new scripts if any
    h.scriptLock.Lock()
    if h.newScripts != nil {
        h.Scripts = h.newScripts  // ← Atomic swap
        h.newScripts = nil
    }
    h.scriptLock.Unlock()
    // ...
}

func (h *ScriptEventHandler) UpdateScripts(scripts []EventScript) {
    h.scriptLock.Lock()
    defer h.scriptLock.Unlock()
    h.newScripts = scripts  // ← Stage for swap
}
```

**Why Critical:**
- Scripts can be hot-reloaded without restarting agent
- Update sets `newScripts`, HandleEvent swaps atomically
- No disruption to running scripts
- Used for configuration reload (SIGHUP)

**Test to Add:**
```csharp
[Fact]
public async Task ScriptHandler_UpdateScripts_HotReloadsOnNextEvent()
{
    // Arrange
    var handler = new ScriptEventHandler(
        () => _selfMember,
        new[] { new EventScript { Filter = new EventFilter("*", ""), Script = "old.sh" } });

    // Act - Update scripts while running
    handler.UpdateScripts(new[] 
    { 
        new EventScript { Filter = new EventFilter("*", ""), Script = "new.sh" } 
    });

    // Trigger event - should use NEW script
    var evt = new MemberEvent { Type = EventType.MemberJoin, Members = new[] { _member1 } };
    handler.HandleEvent(evt);
    await Task.Delay(100);

    // Assert - new.sh executed, not old.sh
    Assert.Contains("new.sh", _executedScripts);
    Assert.DoesNotContain("old.sh", _executedScripts);
}
```

---

### 2. Tag Sanitization for Environment Variables ⚠️ NEW

**File:** `invoke.go` lines 34, 74-76  
**Severity:** 🔴 CRITICAL

```go
var sanitizeTagRegexp = regexp.MustCompile(`[^A-Z0-9_]`)

// ...
sanitizedName := sanitizeTagRegexp.ReplaceAllString(strings.ToUpper(name), "_")
tag_env := fmt.Sprintf("SERF_TAG_%s=%s", sanitizedName, val)
```

**Why Critical:**
- Tag names may have invalid env var characters
- Must sanitize to [A-Z0-9_] only
- Convert to uppercase
- Replace all other chars with underscore
- Example: "my-tag" → "SERF_TAG_MY_TAG"

**Test to Add:**
```csharp
[Theory]
[InlineData("dc", "SERF_TAG_DC")]
[InlineData("my-tag", "SERF_TAG_MY_TAG")]
[InlineData("tag.name", "SERF_TAG_TAG_NAME")]
[InlineData("tag-123", "SERF_TAG_TAG_123")]
[InlineData("_private", "SERF_TAG__PRIVATE")]
public async Task ScriptInvoker_TagSanitization_ConvertsToValidEnvVar(
    string tagName, string expectedEnvVar)
{
    // Arrange
    var self = new Member 
    { 
        Name = "test",
        Tags = new Dictionary<string, string> { [tagName] = "value" }
    };

    // Act
    var envVars = ScriptInvoker.BuildEnvironmentVariables(self, _memberEvent);

    // Assert
    Assert.Contains(envVars, kvp => kvp.Key == expectedEnvVar);
}
```

---

### 3. Output Buffer Limit (8KB) ⚠️ NEW

**File:** `invoke.go` lines 25-28, 47, 109-113  
**Severity:** 🔴 CRITICAL

```go
const maxBufSize = 8 * 1024

output, _ := circbuf.NewBuffer(maxBufSize)

// Warn if buffer is overwritten
if output.TotalWritten() > output.Size() {
    logger.Printf("[WARN] agent: Script '%s' generated %d bytes of output, truncated to %d",
        script, output.TotalWritten(), output.Size())
}
```

**Why Critical:**
- Prevents memory exhaustion from faulty scripts
- Output limited to 8KB
- Excess output truncated with warning
- Must track totalWritten vs bufferSize

**Test to Add:**
```csharp
[Fact]
public async Task ScriptInvoker_OutputExceeds8KB_TruncatesWithWarning()
{
    // Arrange - Script that generates 20KB output
    var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "cmd /c \"for /L %i in (1,1,2000) do @echo This is a long line of output\""
        : "/bin/sh -c 'for i in {1..2000}; do echo \"This is a long line of output\"; done'";

    // Act
    var result = await ScriptInvoker.ExecuteAsync(script, _envVars, null);

    // Assert
    Assert.True(result.TotalWritten > 8192);  // More than 8KB
    Assert.Equal(8192, result.Output.Length);  // Truncated to 8KB
    Assert.True(result.WasTruncated);
    Assert.Contains("truncated", result.Warnings);
}
```

---

### 4. Slow Script Warning (1 Second) ⚠️ NEW

**File:** `invoke.go` lines 30-31, 99-103, 116  
**Severity:** 🟡 IMPORTANT

```go
const warnSlow = time.Second

// Start a timer to warn about slow handlers
slowTimer := time.AfterFunc(warnSlow, func() {
    logger.Printf("[WARN] agent: Script '%s' slow, execution exceeding %v",
        script, warnSlow)
})

// ...
err = cmd.Wait()
slowTimer.Stop()  // ← Cancel if finished quickly
```

**Why Important:**
- Warns if script takes > 1 second
- Timer starts before execution
- Stopped after completion
- Helps identify slow handlers

**Test to Add:**
```csharp
[Fact]
public async Task ScriptInvoker_SlowScript_LogsWarning()
{
    // Arrange - Script that takes 2 seconds
    var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "cmd /c \"timeout /t 2 /nobreak >nul\""
        : "/bin/sh -c 'sleep 2'";

    var warnings = new List<string>();
    var logger = new TestLogger(warnings);

    // Act
    await ScriptInvoker.ExecuteAsync(script, _envVars, null, logger);

    // Assert
    Assert.Contains(warnings, w => w.Contains("slow") && w.Contains("exceeding"));
}
```

---

### 5. Platform-Specific Shell Invocation ⚠️ NEW

**File:** `invoke.go` lines 49-57, 59  
**Severity:** 🔴 CRITICAL

```go
var shell, flag string
if runtime.GOOS == windows {
    shell = "cmd"
    flag = "/C"
} else {
    shell = "/bin/sh"
    flag = "-c"
}

cmd := exec.Command(shell, flag, script)
```

**Why Critical:**
- Windows: `cmd /C script.bat`
- Unix: `/bin/sh -c script.sh`
- Must detect platform
- Flag syntax different

**Test to Add:**
```csharp
[Fact]
public async Task ScriptInvoker_PlatformDetection_UsesCorrectShell()
{
    // Arrange
    var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "echo Windows"
        : "echo Unix";

    // Act
    var result = await ScriptInvoker.ExecuteAsync(script, _envVars, null);

    // Assert
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Assert.Contains("Windows", result.Output);
    }
    else
    {
        Assert.Contains("Unix", result.Output);
    }
}
```

---

### 6. Event Clean Function (Escape Tabs/Newlines) ⚠️ NEW

**File:** `invoke.go` lines 134-139, 161-163  
**Severity:** 🟡 IMPORTANT

```go
func eventClean(v string) string {
    v = strings.Replace(v, "\t", "\\t", -1)
    v = strings.Replace(v, "\n", "\\n", -1)
    return v
}

// Usage:
eventClean(member.Name),
eventClean(member.Tags["role"]),
eventClean(tags)
```

**Why Important:**
- Member names/roles may contain tabs/newlines
- Would break tab-separated format
- Escape to `\t` and `\n` literals

**Test to Add:**
```csharp
[Theory]
[InlineData("normal", "normal")]
[InlineData("with\ttab", "with\\ttab")]
[InlineData("with\nnewline", "with\\nnewline")]
[InlineData("both\t\n", "both\\t\\n")]
public void EventClean_EscapesTabsAndNewlines(string input, string expected)
{
    // Act
    var result = ScriptInvoker.EventClean(input);

    // Assert
    Assert.Equal(expected, result);
}
```

---

### 7. Member Event Stdin Format ⚠️ NEW

**File:** `invoke.go` lines 141-169  
**Severity:** 🔴 CRITICAL

```go
// Format: "NAME\tADDRESS\tROLE\tTAGS" (tab-separated)
_, err := stdin.Write([]byte(fmt.Sprintf(
    "%s\t%s\t%s\t%s\n",
    eventClean(member.Name),
    member.Addr.String(),
    eventClean(member.Tags["role"]),
    eventClean(tags))))
```

**Format:**
- Tab-separated values
- One line per member
- Tags formatted as `tag1=v1,tag2=v2,...`
- Ends with newline

**Test to Add:**
```csharp
[Fact]
public async Task ScriptInvoker_MemberEventStdin_TabSeparatedFormat()
{
    // Arrange
    var members = new[]
    {
        new Member 
        { 
            Name = "node1", 
            Addr = IPAddress.Parse("192.168.1.10"),
            Tags = new Dictionary<string, string> 
            { 
                ["role"] = "web",
                ["dc"] = "us-east"
            }
        }
    };
    var evt = new MemberEvent { Type = EventType.MemberJoin, Members = members };

    // Act
    var stdin = ScriptInvoker.BuildMemberEventStdin(evt);

    // Assert
    var line = stdin.Split('\n')[0];
    var parts = line.Split('\t');
    Assert.Equal(4, parts.Length);
    Assert.Equal("node1", parts[0]);
    Assert.Equal("192.168.1.10", parts[1]);
    Assert.Equal("web", parts[2]);
    Assert.Contains("role=web", parts[3]);
    Assert.Contains("dc=us-east", parts[3]);
}
```

---

### 8. Payload Newline Appending ⚠️ NEW

**File:** `invoke.go` lines 171-187  
**Severity:** 🟡 IMPORTANT

```go
// Append a newline to payload if missing
payload := buf
if len(payload) > 0 && payload[len(payload)-1] != '\n' {
    payload = append(payload, '\n')
}
```

**Why Important:**
- Most shell scripts expect newline-terminated input
- `read` command needs newline
- Appends if missing

**Test to Add:**
```csharp
[Theory]
[InlineData("data", "data\n")]
[InlineData("data\n", "data\n")]  // Already has newline
[InlineData("", "")]  // Empty stays empty
public void ScriptInvoker_PayloadNewline_AppendsIfMissing(
    string input, string expected)
{
    // Act
    var result = ScriptInvoker.PreparePayload(Encoding.UTF8.GetBytes(input));

    // Assert
    Assert.Equal(expected, Encoding.UTF8.GetString(result));
}
```

---

### 9. Query Auto-Response When Output Present ⚠️ NEW

**File:** `invoke.go` lines 123-129  
**Severity:** 🔴 CRITICAL

```go
// If this is a query and we have output, respond
if query, ok := event.(*serf.Query); ok && output.TotalWritten() > 0 {
    if err := query.Respond(output.Bytes()); err != nil {
        logger.Printf("[WARN] agent: Failed to respond to query '%s': %s",
            event.String(), err)
    }
}
```

**Why Critical:**
- Queries auto-respond with script output
- Only if output present (> 0 bytes)
- Failure logged as warning (doesn't fail invocation)

**Test to Add:**
```csharp
[Fact]
public async Task ScriptInvoker_QueryWithOutput_AutoResponds()
{
    // Arrange
    var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "cmd /c \"echo query response\""
        : "/bin/sh -c 'echo query response'";

    var query = new Query { Name = "test", Payload = null };

    // Act
    await ScriptInvoker.ExecuteAsync(script, _envVars, null, query);

    // Assert
    Assert.True(query.WasResponseSent);
    Assert.Contains("query response", Encoding.UTF8.GetString(query.Response));
}

[Fact]
public async Task ScriptInvoker_QueryNoOutput_NoResponse()
{
    // Arrange - Script with no output
    var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "cmd /c \"exit 0\""
        : "/bin/sh -c 'exit 0'";

    var query = new Query { Name = "test", Payload = null };

    // Act
    await ScriptInvoker.ExecuteAsync(script, _envVars, null, query);

    // Assert
    Assert.False(query.WasResponseSent);  // No output = no response
}
```

---

### 10. Multiple Event Types in Comma-Separated Format ⚠️ NEW

**File:** `event_handler.go` lines 164-192 (ParseEventFilter)  
**Severity:** 🟡 IMPORTANT

```go
events := strings.Split(v, ",")
results := make([]EventFilter, 0, len(events))
for _, event := range events {
    // Parse each event
}
```

**Example:** `"member-leave,member-failed=handle-leave.sh"`

Creates TWO filters for same script.

**Test to Add:**
```csharp
[Fact]
public void ParseEventScript_CommaSepar atedEvents_CreatesMultipleFilters()
{
    // Arrange
    var input = "member-leave,member-failed=handle-leave.sh";

    // Act
    var scripts = EventScript.Parse(input);

    // Assert
    Assert.Equal(2, scripts.Count);
    Assert.Equal("member-leave", scripts[0].Filter.Event);
    Assert.Equal("member-failed", scripts[1].Filter.Event);
    Assert.Equal("handle-leave.sh", scripts[0].Script);
    Assert.Equal("handle-leave.sh", scripts[1].Script);
}
```

---

## Critical Implementation Details

### 1. Script Execution in Goroutine ⚠️

**File:** `event_handler.go` lines 50

Scripts execute asynchronously. Don't block event loop.

```csharp
_ = Task.Run(() => InvokeScript(script, self, evt));
```

---

### 2. Valid Event Types ⚠️

**File:** `event_handler.go` lines 109-122

```go
case "member-join":
case "member-leave":
case "member-failed":
case "member-update":
case "member-reap":
case "user":
case "query":
case "*":
```

8 valid event types. Anything else is invalid.

---

### 3. Environment Variables List ⚠️

**File:** `invoke.go` lines 60-76, 88-94

**Always set:**
- `SERF_EVENT` - event type
- `SERF_SELF_NAME` - local node name
- `SERF_SELF_ROLE` - local node role
- `SERF_TAG_*` - all tags (sanitized names)

**User events:**
- `SERF_USER_EVENT` - user event name
- `SERF_USER_LTIME` - event Lamport time

**Queries:**
- `SERF_QUERY_NAME` - query name
- `SERF_QUERY_LTIME` - query Lamport time

---

### 4. Inherit OS Environment ⚠️

**File:** `invoke.go` line 60

```go
cmd.Env = append(os.Environ(), ...)
```

Script inherits all OS environment variables plus Serf-specific ones.

---

### 5. Stderr + Stdout Combined ⚠️

**File:** `invoke.go` lines 65-66

```go
cmd.Stderr = output
cmd.Stdout = output
```

Both streams go to same buffer.

---

### 6. Stdin Written in Goroutine ⚠️

**File:** `invoke.go` lines 86, 90, 94

```go
go memberEventStdin(logger, stdin, &e)
go streamPayload(logger, stdin, e.Payload)
```

Prevents deadlock if script doesn't read stdin.

---

### 7. Defer stdin.Close() ⚠️

**File:** `invoke.go` lines 149, 175

```go
defer stdin.Close()
```

Always close stdin after writing.

---

### 8. Tags Format: comma-separated ⚠️

**File:** `invoke.go` lines 152-156

```go
for name, value := range member.Tags {
    tagPairs = append(tagPairs, fmt.Sprintf("%s=%s", name, value))
}
tags := strings.Join(tagPairs, ",")
```

Format: `role=web,dc=us-east,version=1.0`

---

### 9. Default Logger if Nil ⚠️

**File:** `event_handler.go` lines 40-42

```go
if h.Logger == nil {
    h.Logger = log.New(os.Stderr, "", log.LstdFlags)
}
```

---

### 10. Script Error Logged, Not Fatal ⚠️

**File:** `event_handler.go` lines 50-54

```go
err := invokeEventScript(h.Logger, script.Script, self, e)
if err != nil {
    h.Logger.Printf("[ERR] agent: Error invoking script '%s': %s",
        script.Script, err)
}
```

Script errors logged, don't stop handler.

---

### 11. CircBuf for Output Capture ⚠️

**File:** `invoke.go` line 47

```go
output, _ := circbuf.NewBuffer(maxBufSize)
```

Circular buffer - oldest data overwritten when full.

---

### 12. cmd.Wait() vs cmd.Start() ⚠️

**File:** `invoke.go` lines 105, 115

```go
if err := cmd.Start(); err != nil {
    return err
}
// ... check output size
err = cmd.Wait()
```

Start begins execution, Wait blocks until completion.

---

### 13. Metrics Tracking ⚠️

**File:** `invoke.go` line 46

```go
defer metrics.MeasureSinceWithLabels([]string{"agent", "invoke", script}, time.Now(), nil)
```

Tracks script execution time.

---

### 14. Debug Logging of Output ⚠️

**File:** `invoke.go` lines 117-118

```go
logger.Printf("[DEBUG] agent: Event '%s' script output: %s",
    event.EventType().String(), output.String())
```

All output logged at DEBUG level.

---

## Test Count By Group (Updated)

| Group | Original | Added | New Total |
|-------|----------|-------|-----------|
| 7.1 Filter Parsing | 5 | +1 | 6 |
| 7.2 Filter Matching | 6 | 0 | 6 |
| 7.3 Script Execution | 8 | +4 | 12 |
| 7.4 Environment Vars | 4 | +1 | 5 |
| 7.5 Query Response | 2 | +2 | 4 |
| 7.6 Output Handling (NEW) | 0 | +1 | 1 |
| 7.7 Hot Reload (NEW) | 0 | +1 | 1 |
| **TOTAL** | **25** | **+10** | **35** |

---

## Recommendations

### 1. Use Process Class in C#

```csharp
var psi = new ProcessStartInfo
{
    FileName = shell,
    Arguments = $"{flag} {script}",
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false
};
```

### 2. Implement CircularBuffer

For 8KB output limit with overflow tracking.

### 3. Tag Sanitization Regex

```csharp
private static readonly Regex SanitizeTagRegex = new(@"[^A-Z0-9_]");

public static string SanitizeTagName(string name)
{
    return SanitizeTagRegex.Replace(name.ToUpper(), "_");
}
```

### 4. Platform Detection

```csharp
var (shell, flag) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    ? ("cmd", "/C")
    : ("/bin/sh", "-c");
```

---

## Files to Update

1. ⏳ `PHASE7_EVENT_HANDLERS.md` - Add 10 missing tests
2. ⏳ `PHASES_OVERVIEW.md` - Update test count to 35

---

## Verification Sources

### Go Code Files Reviewed
- ✅ `serf/cmd/serf/command/agent/event_handler.go` (filters, parsing)
- ✅ `serf/cmd/serf/command/agent/invoke.go` (script execution)
- ✅ `serf/cmd/serf/command/agent/event_handler_test.go` (tests)

### DeepWiki Queries
- ✅ Event handler architecture
- ✅ Script execution and environment
- ✅ Filtering mechanisms

---

**Conclusion:** Phase 7 verification found 10 critical missing tests and 14 important implementation details. Most critical: UpdateScripts hot-reload, tag sanitization regex, 8KB output limit, slow script warnings, platform-specific shell invocation, and query auto-response. Updated Phase 7 ready with 35 tests.
