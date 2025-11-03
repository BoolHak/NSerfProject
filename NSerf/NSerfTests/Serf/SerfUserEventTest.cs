// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Phase 9.5: User Events Tests

using FluentAssertions;
using NSerf.Serf;
using NSerf.Serf.Events;
using NSerf.Memberlist.Configuration;
using System.Threading.Channels;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for user event functionality in Serf.
/// Maps to: TestSerf_eventsUser, TestSerf_eventsUser_sizeLimit in serf_test.go
/// </summary>
public class SerfUserEventTest
{
    /// <summary>
    /// Test: User events are properly broadcast and received
    /// Maps to: TestSerf_eventsUser
    /// </summary>
    [Fact]
    public async Task Serf_EventsUser_ShouldBroadcastAndReceive()
    {
        // Arrange - Create event channel
        var eventChannel = Channel.CreateUnbounded<IEvent>();

        var config1 = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        var config2 = new Config
        {
            NodeName = "node2",
            EventCh = eventChannel.Writer,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node2",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config1);
        using var s2 = await NSerf.Serf.Serf.CreateAsync(config2);

        // Both should start with 1 member
        s1.NumMembers().Should().Be(1);
        s2.NumMembers().Should().Be(1);

        // Act - s1 joins s2
        var s2Addr = $"127.0.0.1:{config2.MemberlistConfig.BindPort}";
        await s1.JoinAsync(new[] { s2Addr }, ignoreOld: false);

        // Wait for join to complete
        await TestHelpers.WaitUntilNumNodesAsync(2, TimeSpan.FromSeconds(5), s1, s2);

        // Fire first user event
        await s1.UserEventAsync("event!", System.Text.Encoding.UTF8.GetBytes("test"), false);

        // Give time for gossip to propagate
        await Task.Delay(500);

        // Fire second user event
        await s1.UserEventAsync("second", System.Text.Encoding.UTF8.GetBytes("foobar"), false);

        // Give time for gossip to propagate
        await Task.Delay(500);

        // Collect events
        var userEvents = new List<UserEvent>();
        while (eventChannel.Reader.TryRead(out var evt))
        {
            if (evt is UserEvent userEvent)
            {
                userEvents.Add(userEvent);
            }
        }

        // Assert - Should have received both user events
        userEvents.Should().HaveCountGreaterOrEqualTo(2, "should receive at least the two user events");

        userEvents.Should().Contain(e => e.Name == "event!" &&
            System.Text.Encoding.UTF8.GetString(e.Payload) == "test",
            "should receive the first user event");

        userEvents.Should().Contain(e => e.Name == "second" &&
            System.Text.Encoding.UTF8.GetString(e.Payload) == "foobar",
            "should receive the second user event");

        await s1.ShutdownAsync();
        await s2.ShutdownAsync();
    }

    /// <summary>
    /// Test: User events that exceed size limit are rejected
    /// Maps to: TestSerf_eventsUser_sizeLimit
    /// </summary>
    [Fact]
    public async Task Serf_EventsUser_SizeLimit_ShouldRejectLargeEvents()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            UserEventSizeLimit = 512, // Default size limit
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);
        s1.NumMembers().Should().Be(1);

        // Act & Assert - Large event should fail
        var name = "this is too large an event";
        var payload = new byte[config.UserEventSizeLimit];

        var act = async () => await s1.UserEventAsync(name, payload, false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*user event exceeds*");

        await s1.ShutdownAsync();
    }

    /// <summary>
    /// Test: User event with empty payload should work
    /// </summary>
    [Fact]
    public async Task Serf_EventsUser_EmptyPayload_ShouldWork()
    {
        // Arrange - Create event channel
        var eventChannel = Channel.CreateUnbounded<IEvent>();

        var config = new Config
        {
            NodeName = "node1",
            EventCh = eventChannel.Writer,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);

        // Act - Fire event with empty payload
        await s1.UserEventAsync("empty-event", Array.Empty<byte>(), false);

        // Give time for local processing
        await Task.Delay(50);

        // Collect events
        var userEvents = new List<UserEvent>();
        while (eventChannel.Reader.TryRead(out var evt))
        {
            if (evt is UserEvent userEvent)
            {
                userEvents.Add(userEvent);
            }
        }

        // Assert
        userEvents.Should().ContainSingle(e => e.Name == "empty-event" && e.Payload.Length == 0);

        await s1.ShutdownAsync();
    }

    /// <summary>
    /// Test: User event coalesce flag is preserved
    /// </summary>
    [Fact]
    public async Task Serf_EventsUser_CoalesceFlag_ShouldBePreserved()
    {
        // Arrange - Create event channel
        var eventChannel = Channel.CreateUnbounded<IEvent>();

        var config = new Config
        {
            NodeName = "node1",
            EventCh = eventChannel.Writer,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);

        // Act - Fire event with coalesce=true
        await s1.UserEventAsync("coalesced-event", System.Text.Encoding.UTF8.GetBytes("data"), true);

        // Give time for local processing
        await Task.Delay(50);

        // Collect events
        var userEvents = new List<UserEvent>();
        while (eventChannel.Reader.TryRead(out var evt))
        {
            if (evt is UserEvent userEvent)
            {
                userEvents.Add(userEvent);
            }
        }

        // Assert - Coalesce flag should be true
        userEvents.Should().ContainSingle(e => e.Name == "coalesced-event" && e.Coalesce == true);

        await s1.ShutdownAsync();
    }
}
