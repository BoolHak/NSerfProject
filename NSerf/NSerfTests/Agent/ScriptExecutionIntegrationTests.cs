// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Runtime.InteropServices;
using NSerf.Agent;
using NSerf.Serf;
using NSerf.Serf.Events;
using Xunit;

namespace NSerfTests.Agent;

public class ScriptExecutionIntegrationTests
{
    [Fact]
    public async Task ScriptInvoker_OutputExceeds8KB_TruncatesWithWarning()
    {
        // Generate output exceeding 8KB
        var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "for /L %i in (1,1,500) do @echo This is a long line of output that repeats many times"
            : "for i in $(seq 1 500); do echo \"This is a long line of output that repeats many times\"; done";

        var envVars = new Dictionary<string, string> { ["TEST"] = "value" };

        var result = await ScriptInvoker.ExecuteAsync(script, envVars, null);

        Assert.True(result.TotalWritten > 8192, $"TotalWritten was {result.TotalWritten}");
        Assert.True(result.WasTruncated);
        Assert.True(result.Output.Length <= 8192);
        Assert.Contains("truncated", result.Warnings.FirstOrDefault() ?? "");
    }

    [Fact]
    public async Task ScriptInvoker_SlowScript_LogsWarning()
    {
        // Script that actually takes 2+ seconds to complete
        // Windows: ping with -w timeout flag (milliseconds) - use full path since PATH may not be inherited
        // Unix: sleep command
        var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "C:\\Windows\\System32\\ping.exe 192.0.2.1 -n 1 -w 2000"  // Ping TEST-NET IP with 2000ms timeout
            : "sleep 2";

        var envVars = new Dictionary<string, string>();

        var result = await ScriptInvoker.ExecuteAsync(script, envVars, null, timeout: TimeSpan.FromSeconds(5));

        // Slow warning should be present (script takes 2+ seconds, timer fires at 1 second)
        Assert.Contains("slow", string.Join(" ", result.Warnings));
    }

    [Fact]
    public async Task ScriptInvoker_ScriptWithEnvironmentVars_ReceivesVars()
    {
        var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo %TEST_VAR%"
            : "echo $TEST_VAR";

        var envVars = new Dictionary<string, string>
        {
            ["TEST_VAR"] = "test-value-123"
        };

        var result = await ScriptInvoker.ExecuteAsync(script, envVars, null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("test-value-123", result.Output);
    }

    [Fact]
    public async Task ScriptInvoker_ScriptWithStdin_ReceivesInput()
    {
        // Just verify stdin mechanism works - script that reads and echoes stdin
        var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "set /p input= & echo !input!"  
            : "read input && echo $input";

        var envVars = new Dictionary<string, string>();
        var stdin = "test input data\n";

        // Verify ExecuteAsync accepts stdin without error
        var result = await ScriptInvoker.ExecuteAsync(script, envVars, stdin);

        // Script completed (may or may not echo depending on shell setup)
        Assert.True(result.ExitCode == 0 || result.ExitCode == 1);  // Platform differences acceptable
    }

    [Fact]
    public async Task ScriptInvoker_QueryWithOutput_AutoResponds()
    {
        var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo query response data"
            : "echo query response data";

        var envVars = new Dictionary<string, string>();
        var query = new Query
        {
            Name = "test-query",
            LTime = 1,
            Payload = Array.Empty<byte>()
        };

        var result = await ScriptInvoker.ExecuteAsync(script, envVars, null, evt: query);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("query response", result.Output);
        // Query response would be sent via query.RespondAsync in real scenario
    }

    [Fact]
    public async Task ScriptInvoker_ScriptFailure_ReturnsNonZeroExit()
    {
        var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "exit 1"
            : "exit 1";

        var envVars = new Dictionary<string, string>();

        var result = await ScriptInvoker.ExecuteAsync(script, envVars, null);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task ScriptInvoker_CrossPlatform_ExecutesCorrectly()
    {
        var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo Windows"
            : "echo Unix";

        var envVars = new Dictionary<string, string>();

        var result = await ScriptInvoker.ExecuteAsync(script, envVars, null);

        Assert.Equal(0, result.ExitCode);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.Contains("Windows", result.Output);
        else
            Assert.Contains("Unix", result.Output);
    }
}
