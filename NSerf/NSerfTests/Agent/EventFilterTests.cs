// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.Serf;
using NSerf.Serf.Events;
using Xunit;

namespace NSerfTests.Agent;

public class EventFilterTests
{
    [Fact]
    public void EventFilter_Parse_SimpleEvent()
    {
        var filter = EventFilter.Parse("member-join");
        
        Assert.Equal("member-join", filter.Event);
        Assert.Equal("", filter.Name);
    }

    [Fact]
    public void EventFilter_Parse_EventWithName()
    {
        var filter = EventFilter.Parse("user:deploy");
        
        Assert.Equal("user", filter.Event);
        Assert.Equal("deploy", filter.Name);
    }

    [Fact]
    public void EventFilter_Parse_QueryWithName()
    {
        var filter = EventFilter.Parse("query:health");
        
        Assert.Equal("query", filter.Event);
        Assert.Equal("health", filter.Name);
    }

    [Fact]
    public void EventFilter_Parse_Wildcard()
    {
        var filter = EventFilter.Parse("*");
        
        Assert.Equal("*", filter.Event);
        Assert.Equal("", filter.Name);
    }

    [Fact]
    public void EventFilter_Parse_InvalidEventType_Throws()
    {
        Assert.Throws<ArgumentException>(() => EventFilter.Parse("invalid-event"));
    }

    [Fact]
    public void EventFilter_Wildcard_MatchesAllEvents()
    {
        var filter = new EventFilter("*", "");
        
        var memberEvt = new MemberEvent { Type = EventType.MemberJoin, Members = new List<Member>() };
        var userEvt = new UserEvent { Name = "test", Payload = Array.Empty<byte>() };
        
        Assert.True(filter.Matches(memberEvt));
        Assert.True(filter.Matches(userEvt));
    }

    [Fact]
    public void EventFilter_MemberJoin_MatchesOnlyJoins()
    {
        var filter = new EventFilter("member-join", "");
        
        var joinEvt = new MemberEvent { Type = EventType.MemberJoin, Members = new List<Member>() };
        var leaveEvt = new MemberEvent { Type = EventType.MemberLeave, Members = new List<Member>() };
        
        Assert.True(filter.Matches(joinEvt));
        Assert.False(filter.Matches(leaveEvt));
    }

    [Fact]
    public void EventFilter_UserEventWithName_FiltersCorrectly()
    {
        var filter = new EventFilter("user", "deploy");
        
        var deployEvt = new UserEvent { Name = "deploy", Payload = Array.Empty<byte>() };
        var restartEvt = new UserEvent { Name = "restart", Payload = Array.Empty<byte>() };
        
        Assert.True(filter.Matches(deployEvt));
        Assert.False(filter.Matches(restartEvt));
    }
}
