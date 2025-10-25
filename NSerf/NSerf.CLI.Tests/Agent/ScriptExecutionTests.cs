// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.Serf.Events;
using System.Text;

namespace NSerf.CLI.Tests.Agent;

/// <summary>
/// Tests for script execution functionality
/// Ported from Go's cmd/serf/command/agent/agent_test.go
/// </summary>
[Trait("Category", "Integration")]
public class ScriptExecutionTests
{
    [Fact]
    public async Task ScriptInvoker_ExecutesScript_ReturnsOutput()
    {
        // Create a simple test script
        var isWindows = OperatingSystem.IsWindows();
        var scriptContent = isWindows 
            ? "@echo off\necho Hello from script" 
            : "#!/bin/sh\necho Hello from script";
        
        var scriptPath = Path.Combine(Path.GetTempPath(), $"test-script-{Guid.NewGuid()}.{(isWindows ? "bat" : "sh")}");
        
        try
        {
            await File.WriteAllTextAsync(scriptPath, scriptContent);
            if (!isWindows)
            {
                // Make executable on Unix
                var chmodInfo = new System.Diagnostics.ProcessStartInfo("chmod", $"+x {scriptPath}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var chmodProc = System.Diagnostics.Process.Start(chmodInfo);
                await chmodProc!.WaitForExitAsync();
            }

            var envVars = new Dictionary<string, string>
            {
                ["SERF_EVENT"] = "user",
                ["SERF_SELF_NAME"] = "test-node",
                ["SERF_TAG_ROLE"] = "TEST"
            };

            var result = await ScriptInvoker.ExecuteAsync(scriptPath, envVars, null);
            var output = result.Output;

            Assert.Contains("Hello from script", output);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    [Fact]
    public async Task ScriptInvoker_PassesStdin_ScriptReceivesPayload()
    {
        var isWindows = OperatingSystem.IsWindows();
        var scriptContent = isWindows
            ? "@echo off\nset /p INPUT=\necho Received: %INPUT%"
            : "#!/bin/sh\nread INPUT\necho \"Received: $INPUT\"";
        
        var scriptPath = Path.Combine(Path.GetTempPath(), $"test-stdin-{Guid.NewGuid()}.{(isWindows ? "bat" : "sh")}");
        
        try
        {
            await File.WriteAllTextAsync(scriptPath, scriptContent);
            if (!isWindows)
            {
                var chmodInfo = new System.Diagnostics.ProcessStartInfo("chmod", $"+x {scriptPath}") 
                { 
                    CreateNoWindow = true, 
                    UseShellExecute = false 
                };
                using var chmodProc = System.Diagnostics.Process.Start(chmodInfo);
                await chmodProc!.WaitForExitAsync();
            }

            var payload = "test-payload-data";
            var envVars = new Dictionary<string, string>
            {
                ["SERF_EVENT"] = "user",
                ["SERF_SELF_NAME"] = "test-node"
            };

            var result = await ScriptInvoker.ExecuteAsync(scriptPath, envVars, payload);
            var output = result.Output;

            Assert.Contains("test-payload-data", output);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    [Fact]
    public async Task ScriptInvoker_SetsEnvironmentVariables()
    {
        var isWindows = OperatingSystem.IsWindows();
        var scriptContent = isWindows
            ? "@echo off\necho Event: %SERF_EVENT%\necho Name: %SERF_SELF_NAME%\necho Role: %SERF_TAG_ROLE%"
            : "#!/bin/sh\necho \"Event: $SERF_EVENT\"\necho \"Name: $SERF_SELF_NAME\"\necho \"Role: $SERF_TAG_ROLE\"";
        
        var scriptPath = Path.Combine(Path.GetTempPath(), $"test-env-{Guid.NewGuid()}.{(isWindows ? "bat" : "sh")}");
        
        try
        {
            await File.WriteAllTextAsync(scriptPath, scriptContent);
            if (!isWindows)
            {
                var chmodInfo = new System.Diagnostics.ProcessStartInfo("chmod", $"+x {scriptPath}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var chmodProc = System.Diagnostics.Process.Start(chmodInfo);
                await chmodProc!.WaitForExitAsync();
            }

            var envVars = new Dictionary<string, string>
            {
                ["SERF_EVENT"] = "user",
                ["SERF_SELF_NAME"] = "my-node",
                ["SERF_TAG_ROLE"] = "WEB_SERVER"
            };

            var result = await ScriptInvoker.ExecuteAsync(scriptPath, envVars, null);
            var output = result.Output;

            Assert.Contains("Event: user", output);
            Assert.Contains("Name: my-node", output);
            Assert.Contains("Role: WEB_SERVER", output); // Tags are sanitized to uppercase
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    [Fact]
    public async Task ScriptInvoker_OutputTruncation_LargeOutput()
    {
        var isWindows = OperatingSystem.IsWindows();
        // Generate output larger than 8KB
        var scriptContent = isWindows
            ? "@echo off\nfor /L %%i in (1,1,1000) do echo This is line %%i with some padding text to make it longer"
            : "#!/bin/sh\nfor i in $(seq 1 1000); do echo \"This is line $i with some padding text to make it longer\"; done";
        
        var scriptPath = Path.Combine(Path.GetTempPath(), $"test-output-{Guid.NewGuid()}.{(isWindows ? "bat" : "sh")}");
        
        try
        {
            await File.WriteAllTextAsync(scriptPath, scriptContent);
            if (!isWindows)
            {
                var chmodInfo = new System.Diagnostics.ProcessStartInfo("chmod", $"+x {scriptPath}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var chmodProc = System.Diagnostics.Process.Start(chmodInfo);
                await chmodProc!.WaitForExitAsync();
            }

            var envVars = new Dictionary<string, string>
            {
                ["SERF_EVENT"] = "user",
                ["SERF_SELF_NAME"] = "test-node"
            };

            var result = await ScriptInvoker.ExecuteAsync(scriptPath, envVars, null);
            var output = result.Output;

            // Output should be captured but may be truncated
            // CircularBuffer size is 8KB
            Assert.NotEmpty(output);
            Assert.True(output.Length <= 8192 + 1024); // Allow some overhead
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    [Fact]
    public async Task ScriptInvoker_SlowScript_CompletesWithWarning()
    {
        var isWindows = OperatingSystem.IsWindows();
        // Script that takes 2 seconds
        var scriptContent = isWindows
            ? "@echo off\ntimeout /t 2 /nobreak > nul\necho Completed"
            : "#!/bin/sh\nsleep 2\necho Completed";
        
        var scriptPath = Path.Combine(Path.GetTempPath(), $"test-slow-{Guid.NewGuid()}.{(isWindows ? "bat" : "sh")}");
        
        try
        {
            await File.WriteAllTextAsync(scriptPath, scriptContent);
            if (!isWindows)
            {
                var chmodInfo = new System.Diagnostics.ProcessStartInfo("chmod", $"+x {scriptPath}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var chmodProc = System.Diagnostics.Process.Start(chmodInfo);
                await chmodProc!.WaitForExitAsync();
            }

            var envVars = new Dictionary<string, string>
            {
                ["SERF_EVENT"] = "user",
                ["SERF_SELF_NAME"] = "test-node"
            };

            var startTime = DateTime.UtcNow;
            var result = await ScriptInvoker.ExecuteAsync(scriptPath, envVars, null);
            var duration = DateTime.UtcNow - startTime;
            var output = result.Output;

            // Should complete but take > 1 second (slow script threshold)
            Assert.Contains("Completed", output);
            Assert.True(duration.TotalSeconds >= 2);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    [Fact]
    public async Task ScriptInvoker_FailingScript_ReturnsOutput()
    {
        var isWindows = OperatingSystem.IsWindows();
        var scriptContent = isWindows
            ? "@echo off\necho Script failed\nexit /b 1"
            : "#!/bin/sh\necho \"Script failed\"\nexit 1";
        
        var scriptPath = Path.Combine(Path.GetTempPath(), $"test-fail-{Guid.NewGuid()}.{(isWindows ? "bat" : "sh")}");
        
        try
        {
            await File.WriteAllTextAsync(scriptPath, scriptContent);
            if (!isWindows)
            {
                var chmodInfo = new System.Diagnostics.ProcessStartInfo("chmod", $"+x {scriptPath}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var chmodProc = System.Diagnostics.Process.Start(chmodInfo);
                await chmodProc!.WaitForExitAsync();
            }

            var envVars = new Dictionary<string, string>
            {
                ["SERF_EVENT"] = "user",
                ["SERF_SELF_NAME"] = "test-node"
            };

            // Should not throw - failing scripts just return their output
            var result = await ScriptInvoker.ExecuteAsync(scriptPath, envVars, null);
            var output = result.Output;

            Assert.Contains("Script failed", output);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }
}
