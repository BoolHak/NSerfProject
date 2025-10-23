// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class EventScriptParseTests
{
    [Fact]
    public void EventScript_Parse_JustScript_MatchesAll()
    {
        var scripts = EventScript.Parse("handler.sh");
        
        Assert.Single(scripts);
        Assert.Equal("*", scripts[0].Filter.Event);
        Assert.Equal("handler.sh", scripts[0].Script);
    }

    [Fact]
    public void EventScript_Parse_EventAndScript()
    {
        var scripts = EventScript.Parse("member-join=join-handler.sh");
        
        Assert.Single(scripts);
        Assert.Equal("member-join", scripts[0].Filter.Event);
        Assert.Equal("join-handler.sh", scripts[0].Script);
    }

    [Fact]
    public void EventScript_Parse_EventWithNameAndScript()
    {
        var scripts = EventScript.Parse("user:deploy=deploy-handler.sh");
        
        Assert.Single(scripts);
        Assert.Equal("user", scripts[0].Filter.Event);
        Assert.Equal("deploy", scripts[0].Filter.Name);
        Assert.Equal("deploy-handler.sh", scripts[0].Script);
    }

    [Fact]
    public void EventScript_Parse_CommaSeparatedEvents_CreatesMultiple()
    {
        var scripts = EventScript.Parse("member-leave,member-failed=handle-leave.sh");
        
        Assert.Equal(2, scripts.Count);
        Assert.Equal("member-leave", scripts[0].Filter.Event);
        Assert.Equal("member-failed", scripts[1].Filter.Event);
        Assert.Equal("handle-leave.sh", scripts[0].Script);
        Assert.Equal("handle-leave.sh", scripts[1].Script);
    }

    [Fact]
    public void EventScript_Parse_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => EventScript.Parse(""));
    }

    [Fact]
    public void EventScript_Parse_WhitespaceOnly_Throws()
    {
        Assert.Throws<ArgumentException>(() => EventScript.Parse("   "));
    }
}
