// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Runtime.InteropServices;
using NSerf.Agent;
using NSerf.Serf;
using NSerf.Serf.Events;
using Xunit;

namespace NSerfTests.Agent;

public class ScriptInvokerTests
{
    [Theory]
    [InlineData("dc", "SERF_TAG_DC")]
    [InlineData("my-tag", "SERF_TAG_MY_TAG")]
    [InlineData("tag.name", "SERF_TAG_TAG_NAME")]
    [InlineData("tag-123", "SERF_TAG_TAG_123")]
    [InlineData("_private", "SERF_TAG__PRIVATE")]
    public void ScriptInvoker_TagSanitization_ConvertsToValidEnvVar(
        string tagName, string expectedEnvVar)
    {
        var self = new Member
        {
            Name = "test-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 7373,
            Tags = new Dictionary<string, string> { [tagName] = "value" }
        };

        var memberEvt = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member> { self }
        };

        var envVars = ScriptInvoker.BuildEnvironmentVariables(self, memberEvt);

        Assert.Contains(envVars, kvp => kvp.Key == expectedEnvVar && kvp.Value == "value");
    }

    [Fact]
    public void ScriptInvoker_BuildEnvironmentVariables_SetsBasicVars()
    {
        var self = new Member
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.10"),
            Port = 7373,
            Tags = new Dictionary<string, string>
            {
                ["role"] = "web",
                ["dc"] = "us-east"
            }
        };

        var memberEvt = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member>()
        };

        var envVars = ScriptInvoker.BuildEnvironmentVariables(self, memberEvt);

        Assert.Equal("member-join", envVars["SERF_EVENT"]);
        Assert.Equal("node1", envVars["SERF_SELF_NAME"]);
        Assert.Equal("web", envVars["SERF_SELF_ROLE"]);
        Assert.Equal("web", envVars["SERF_TAG_ROLE"]);
        Assert.Equal("us-east", envVars["SERF_TAG_DC"]);
    }

    [Fact]
    public void ScriptInvoker_BuildMemberEventStdin_TabSeparatedFormat()
    {
        var member = new Member
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.10"),
            Port = 7373,
            Tags = new Dictionary<string, string>
            {
                ["role"] = "web",
                ["dc"] = "us-east"
            }
        };

        var evt = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member> { member }
        };

        var stdin = ScriptInvoker.BuildMemberEventStdin(evt);

        var line = stdin.Split('\n')[0];
        var parts = line.Split('\t');
        
        Assert.Equal(4, parts.Length);
        Assert.Equal("node1", parts[0]);
        Assert.Equal("192.168.1.10", parts[1]);
        Assert.Equal("web", parts[2]);
        Assert.Contains("role=web", parts[3]);
        Assert.Contains("dc=us-east", parts[3]);
    }

    [Theory]
    [InlineData("normal", "normal")]
    [InlineData("with\ttab", "with\\ttab")]
    [InlineData("with\nnewline", "with\\nnewline")]
    [InlineData("both\t\n", "both\\t\\n")]
    public void ScriptInvoker_EventClean_EscapesTabsAndNewlines(string input, string expected)
    {
        var result = ScriptInvoker.EventClean(input);
        
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("data", "data\n")]
    [InlineData("data\n", "data\n")]
    [InlineData("", "")]
    public void ScriptInvoker_PreparePayload_AppendsNewlineIfMissing(string input, string expected)
    {
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        var result = ScriptInvoker.PreparePayload(inputBytes);
        
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ScriptInvoker_SimpleScript_Executes()
    {
        var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo test"
            : "echo test";

        var envVars = new Dictionary<string, string>
        {
            ["TEST_VAR"] = "value"
        };

        var result = await ScriptInvoker.ExecuteAsync(script, envVars, null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("test", result.Output);
    }
}
