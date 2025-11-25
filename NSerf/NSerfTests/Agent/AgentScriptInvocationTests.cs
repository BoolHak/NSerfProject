using System.Runtime.InteropServices;
using NSerf.Agent;
using NSerf.Serf.Events;

namespace NSerfTests.Agent;

public class AgentScriptInvocationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _markerFile;

    public AgentScriptInvocationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"serf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _markerFile = Path.Combine(_tempDir, "script-executed.txt");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private string CreateTestScript()
    {
        string scriptPath;
        string scriptContent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptPath = Path.Combine(_tempDir, "test-handler.bat");
            scriptContent = $"""
                @echo off
                echo %SERF_EVENT% > "{_markerFile}"
                """;
        }
        else
        {
            scriptPath = Path.Combine(_tempDir, "test-handler.sh");
            scriptContent = $"""
                #!/bin/bash
                echo "$SERF_EVENT" > "{_markerFile}"
                """;
        }

        File.WriteAllText(scriptPath, scriptContent);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = System.Diagnostics.Process.Start("chmod", $"+x \"{scriptPath}\"");
            chmod?.WaitForExit();
        }

        return scriptPath;
    }

    [Fact]
    public async Task Agent_WithEventHandler_ShouldInvokeScriptOnMemberJoin()
    {
        var scriptPath = CreateTestScript();

        var config = new AgentConfig
        {
            NodeName = "script-test-node",
            BindAddr = "127.0.0.1:0",
            EventHandlers = [$"member-join={scriptPath}"]
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        await Task.Delay(2000);

        Assert.True(File.Exists(_markerFile), $"Script was not executed. Marker file not found: {_markerFile}");

        var content = await File.ReadAllTextAsync(_markerFile);
        Assert.Contains("member-join", content.Trim());

        await agent.ShutdownAsync();
    }

    [Fact]
    public async Task Agent_WithWildcardEventHandler_ShouldInvokeScriptOnAnyEvent()
    {
        var scriptPath = CreateTestScript();

        var config = new AgentConfig
        {
            NodeName = "wildcard-script-test",
            BindAddr = "127.0.0.1:0",
            EventHandlers = [scriptPath]
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        await Task.Delay(2000);

        Assert.True(File.Exists(_markerFile), $"Script was not executed. Marker file not found: {_markerFile}");

        await agent.ShutdownAsync();
    }

    [Fact]
    public async Task Agent_WithUserEventHandler_ShouldInvokeScriptOnUserEvent()
    {
        string scriptPath;
        string scriptContent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptPath = Path.Combine(_tempDir, "user-handler.bat");
            scriptContent = $"""
                @echo off
                echo %SERF_USER_EVENT% > "{_markerFile}"
                """;
        }
        else
        {
            scriptPath = Path.Combine(_tempDir, "user-handler.sh");
            scriptContent = $"""
                #!/bin/bash
                echo "$SERF_USER_EVENT" > "{_markerFile}"
                """;
        }

        File.WriteAllText(scriptPath, scriptContent);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = System.Diagnostics.Process.Start("chmod", $"+x \"{scriptPath}\"");
            chmod?.WaitForExit();
        }

        var config = new AgentConfig
        {
            NodeName = "user-event-script-test",
            BindAddr = "127.0.0.1:0",
            EventHandlers = [$"user:test-event={scriptPath}"]
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);
        await agent.Serf.UserEventAsync("test-event", [], false);

        await Task.Delay(2000);

        Assert.True(File.Exists(_markerFile), $"Script was not executed for user event. Marker file not found: {_markerFile}");

        var content = await File.ReadAllTextAsync(_markerFile);
        Assert.Contains("test-event", content.Trim());

        await agent.ShutdownAsync();
    }

    [Fact]
    public void EventHandlers_ParsedCorrectly_ShouldCreateEventScripts()
    {
        var specs = new[]
        {
            "member-join=join.sh",
            "member-leave,member-failed=leave.sh",
            "user:deploy=deploy.sh",
            "all-events.sh"
        };

        var allScripts = new List<EventScript>();
        foreach (var spec in specs)
        {
            allScripts.AddRange(EventScript.Parse(spec));
        }

        Assert.Equal(5, allScripts.Count);

        Assert.Equal("member-join", allScripts[0].Filter.Event);
        Assert.Equal("join.sh", allScripts[0].Script);

        Assert.Equal("member-leave", allScripts[1].Filter.Event);
        Assert.Equal("leave.sh", allScripts[1].Script);

        Assert.Equal("member-failed", allScripts[2].Filter.Event);
        Assert.Equal("leave.sh", allScripts[2].Script);

        Assert.Equal("user", allScripts[3].Filter.Event);
        Assert.Equal("deploy", allScripts[3].Filter.Name);
        Assert.Equal("deploy.sh", allScripts[3].Script);

        Assert.Equal("*", allScripts[4].Filter.Event);
        Assert.Equal("all-events.sh", allScripts[4].Script);
    }

    [Fact]
    public void ScriptEventHandler_RegisteredWithAgent_ShouldReceiveEvents()
    {
        var receivedEvents = new List<IEvent>();
        var handler = new TestEventHandler(receivedEvents);

        var config = new AgentConfig
        {
            NodeName = "handler-test",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        agent.RegisterEventHandler(handler);

        var testEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = []
        };

        handler.HandleEvent(testEvent);

        Assert.Single(receivedEvents);
        Assert.IsType<MemberEvent>(receivedEvents[0]);
    }

    private class TestEventHandler(List<IEvent> receivedEvents) : IEventHandler
    {
        public void HandleEvent(IEvent evt)
        {
            receivedEvents.Add(evt);
        }
    }

    [Fact]
    public async Task Agent_WithAbsoluteScriptPath_ShouldInvokeScript()
    {
        string scriptPath;
        string scriptContent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptPath = Path.Combine(_tempDir, "absolute-test.bat");
            scriptContent = $"""
                @echo off
                echo SERF_SELF_NAME=%SERF_SELF_NAME% > "{_markerFile}"
                """;
        }
        else
        {
            scriptPath = Path.Combine(_tempDir, "absolute-test.sh");
            scriptContent = $"""
                #!/bin/bash
                echo "SERF_SELF_NAME=$SERF_SELF_NAME" > "{_markerFile}"
                """;
        }

        File.WriteAllText(scriptPath, scriptContent);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = System.Diagnostics.Process.Start("chmod", $"+x \"{scriptPath}\"");
            chmod?.WaitForExit();
        }

        var config = new AgentConfig
        {
            NodeName = "absolute-path-test-node",
            BindAddr = "127.0.0.1:0",
            EventHandlers = [$"member-join={scriptPath}"]
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        await Task.Delay(2000);

        Assert.True(File.Exists(_markerFile), $"Script was not executed. Marker file not found: {_markerFile}");

        var content = await File.ReadAllTextAsync(_markerFile);
        Assert.Contains("absolute-path-test-node", content);

        await agent.ShutdownAsync();
    }

    [Fact]
    public async Task Agent_ScriptReceivesCorrectEnvironmentVariables()
    {
        string scriptPath;
        string scriptContent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptPath = Path.Combine(_tempDir, "env-test.bat");
            scriptContent = $"""
                @echo off
                echo SERF_EVENT=%SERF_EVENT% >> "{_markerFile}"
                echo SERF_SELF_NAME=%SERF_SELF_NAME% >> "{_markerFile}"
                """;
        }
        else
        {
            scriptPath = Path.Combine(_tempDir, "env-test.sh");
            scriptContent = $"""
                #!/bin/bash
                echo "SERF_EVENT=$SERF_EVENT" >> "{_markerFile}"
                echo "SERF_SELF_NAME=$SERF_SELF_NAME" >> "{_markerFile}"
                """;
        }

        File.WriteAllText(scriptPath, scriptContent);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = System.Diagnostics.Process.Start("chmod", $"+x \"{scriptPath}\"");
            chmod?.WaitForExit();
        }

        var config = new AgentConfig
        {
            NodeName = "env-vars-test-node",
            BindAddr = "127.0.0.1:0",
            EventHandlers = [$"member-join={scriptPath}"]
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        await Task.Delay(2000);

        Assert.True(File.Exists(_markerFile), $"Script was not executed. Marker file not found: {_markerFile}");

        var content = await File.ReadAllTextAsync(_markerFile);
        Assert.Contains("member-join", content);
        Assert.Contains("env-vars-test-node", content);

        await agent.ShutdownAsync();
    }

    [Fact]
    public async Task Agent_MultipleEventHandlers_AllShouldBeInvoked()
    {
        var markerFile1 = Path.Combine(_tempDir, "marker1.txt");
        var markerFile2 = Path.Combine(_tempDir, "marker2.txt");

        string script1Path, script2Path;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            script1Path = Path.Combine(_tempDir, "handler1.bat");
            script2Path = Path.Combine(_tempDir, "handler2.bat");
            File.WriteAllText(script1Path, $"@echo off\necho handler1 > \"{markerFile1}\"");
            File.WriteAllText(script2Path, $"@echo off\necho handler2 > \"{markerFile2}\"");
        }
        else
        {
            script1Path = Path.Combine(_tempDir, "handler1.sh");
            script2Path = Path.Combine(_tempDir, "handler2.sh");
            File.WriteAllText(script1Path, $"#!/bin/bash\necho handler1 > \"{markerFile1}\"");
            File.WriteAllText(script2Path, $"#!/bin/bash\necho handler2 > \"{markerFile2}\"");

            System.Diagnostics.Process.Start("chmod", $"+x \"{script1Path}\"")?.WaitForExit();
            System.Diagnostics.Process.Start("chmod", $"+x \"{script2Path}\"")?.WaitForExit();
        }

        var config = new AgentConfig
        {
            NodeName = "multi-handler-test",
            BindAddr = "127.0.0.1:0",
            EventHandlers =
            [
                $"member-join={script1Path}",
                $"member-join={script2Path}"
            ]
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        await Task.Delay(2000);

        Assert.True(File.Exists(markerFile1), "First handler was not executed");
        Assert.True(File.Exists(markerFile2), "Second handler was not executed");

        await agent.ShutdownAsync();
    }

    [Fact]
    public async Task Agent_LoadEventHandlersFromConfigFile_ShouldInvokeScript()
    {
        var scriptPath = CreateTestScript();

        var configFile = Path.Combine(_tempDir, "config.json");
        var configContent = $$"""
            {
                "node_name": "config-file-test",
                "bind_addr": "127.0.0.1:0",
                "event_handlers": ["member-join={{scriptPath.Replace("\\", "\\\\")}}"]
            }
            """;

        await File.WriteAllTextAsync(configFile, configContent);

        var config = await ConfigLoader.LoadFromFileAsync(configFile);

        Assert.Single(config.EventHandlers);
        Assert.Contains(scriptPath, config.EventHandlers[0]);

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        await Task.Delay(2000);

        Assert.True(File.Exists(_markerFile), $"Script was not executed from config file. Marker file not found: {_markerFile}");

        await agent.ShutdownAsync();
    }

    [Fact]
    public async Task ConfigLoader_EventHandlers_ShouldDeserializeCorrectly()
    {
        var configFile = Path.Combine(_tempDir, "handlers-config.json");
        var configContent = """
            {
                "node_name": "test-node",
                "event_handlers": [
                    "member-join=join.sh",
                    "member-leave=leave.sh",
                    "user:deploy=deploy.sh"
                ]
            }
            """;

        await File.WriteAllTextAsync(configFile, configContent);

        var config = await ConfigLoader.LoadFromFileAsync(configFile);

        Assert.Equal(3, config.EventHandlers.Count);
        Assert.Equal("member-join=join.sh", config.EventHandlers[0]);
        Assert.Equal("member-leave=leave.sh", config.EventHandlers[1]);
        Assert.Equal("user:deploy=deploy.sh", config.EventHandlers[2]);
    }
}
