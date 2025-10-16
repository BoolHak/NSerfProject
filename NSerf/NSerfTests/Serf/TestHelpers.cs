// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Threading.Channels;
using NSerf.Serf;
using NSerf.Serf.Events;
using NSerf.Memberlist.Configuration;

namespace NSerfTests.Serf;

/// <summary>
/// Reusable test helper utilities for Serf testing.
/// Provides common test configuration, waiting helpers, and validation methods.
/// </summary>
public static class TestHelpers
{
    private static int _portCounter = 5000;
    private static readonly object _portLock = new();

    /// <summary>
    /// Creates a test Serf configuration with aggressive timeouts for faster tests.
    /// Each call gets a unique port number for parallel test execution.
    /// </summary>
    /// <param name="nodeName">Optional node name. If not provided, generates unique name.</param>
    /// <returns>Configured SerfConfig for testing</returns>
    public static SerfConfig CreateTestConfig(string? nodeName = null)
    {
        int port;
        lock (_portLock)
        {
            port = ++_portCounter;
        }

        nodeName ??= $"test-node-{port}";

        return new SerfConfig
        {
            NodeName = nodeName,
            MemberlistConfig = new MemberlistConfig
            {
                Name = nodeName,
                BindAddr = "127.0.0.1",
                BindPort = port,
                AdvertiseAddr = "127.0.0.1",
                AdvertisePort = port,
                
                // Aggressive test timeouts for faster convergence
                ProbeInterval = TimeSpan.FromMilliseconds(50),
                ProbeTimeout = TimeSpan.FromMilliseconds(25),
                GossipInterval = TimeSpan.FromMilliseconds(5),
                TCPTimeout = TimeSpan.FromMilliseconds(100),
                SuspicionMult = 1,
                
                // Require node names for strict validation
                RequireNodeNames = true
            },
            
            // Short intervals for testing
            ReapInterval = TimeSpan.FromSeconds(1),
            ReconnectInterval = TimeSpan.FromMilliseconds(100),
            ReconnectTimeout = TimeSpan.FromMicroseconds(1),
            TombstoneTimeout = TimeSpan.FromMicroseconds(1)
        };
    }

    /// <summary>
    /// Waits until all Serf instances have the expected number of members.
    /// Uses polling with configurable timeout.
    /// </summary>
    /// <param name="expected">Expected number of members</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="instances">Serf instances to check</param>
    /// <exception cref="TimeoutException">Thrown if timeout is exceeded</exception>
    public static async Task WaitUntilNumNodesAsync(
        int expected,
        TimeSpan timeout,
        params NSerf.Serf.Serf[] instances)
    {
        using var cts = new CancellationTokenSource(timeout);
        
        while (!cts.Token.IsCancellationRequested)
        {
            if (instances.All(s => s.NumMembers() == expected))
            {
                return;
            }

            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Provide helpful error message
        var counts = string.Join(", ", instances.Select(s => s.NumMembers()));
        throw new TimeoutException(
            $"Timeout waiting for {expected} nodes. Current counts: [{counts}]");
    }

    /// <summary>
    /// Waits until all Serf instances have the expected number of members.
    /// Uses default timeout of 10 seconds.
    /// </summary>
    public static Task WaitUntilNumNodesAsync(int expected, params NSerf.Serf.Serf[] instances)
    {
        return WaitUntilNumNodesAsync(expected, TimeSpan.FromSeconds(10), instances);
    }

    /// <summary>
    /// Verifies that a specific member is in the expected status within a member list.
    /// </summary>
    /// <param name="members">List of members to search</param>
    /// <param name="name">Name of the member to find</param>
    /// <param name="status">Expected status</param>
    /// <exception cref="XunitException">Thrown if member not found or has wrong status</exception>
    public static void TestMember(List<Member> members, string name, MemberStatus status)
    {
        var member = members.FirstOrDefault(m => m.Name == name);

        if (status == MemberStatus.None)
        {
            // We expect NOT to find it
            member.Should().BeNull($"member {name} should not exist");
            return;
        }

        member.Should().NotBeNull($"member {name} should exist");
        member!.Status.Should().Be(status, $"member {name} should have status {status}");
    }

    /// <summary>
    /// Verifies that a sequence of events occurred for a specific node.
    /// Reads from event channel and filters for the target node.
    /// </summary>
    /// <param name="eventChannel">Channel to read events from</param>
    /// <param name="nodeName">Node name to filter events for</param>
    /// <param name="expectedEvents">Expected sequence of event types</param>
    /// <param name="timeout">Maximum time to wait for events</param>
    public static async Task TestEventsAsync(
        ChannelReader<Event> eventChannel,
        string nodeName,
        EventType[] expectedEvents,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        var actualEvents = new List<EventType>();

        using var cts = new CancellationTokenSource(timeout.Value);

        try
        {
            await foreach (var evt in eventChannel.ReadAllAsync(cts.Token))
            {
                if (evt is MemberEvent memberEvent)
                {
                    // Check if this event contains our target node
                    var member = memberEvent.Members.FirstOrDefault(m => m.Name == nodeName);
                    if (member != null)
                    {
                        actualEvents.Add(memberEvent.Type);
                        
                        // Stop if we've collected enough events
                        if (actualEvents.Count >= expectedEvents.Length)
                        {
                            break;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached, compare what we have
        }

        actualEvents.Should().Equal(expectedEvents,
            $"Expected event sequence for {nodeName}");
    }

    /// <summary>
    /// Verifies that expected user events occurred with specific names and payloads.
    /// </summary>
    public static async Task TestUserEventsAsync(
        ChannelReader<Event> eventChannel,
        string[] expectedNames,
        byte[][] expectedPayloads,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        var actualNames = new List<string>();
        var actualPayloads = new List<byte[]>();

        using var cts = new CancellationTokenSource(timeout.Value);

        try
        {
            await foreach (var evt in eventChannel.ReadAllAsync(cts.Token))
            {
                if (evt is UserEvent userEvent)
                {
                    actualNames.Add(userEvent.Name);
                    actualPayloads.Add(userEvent.Payload);
                    
                    if (actualNames.Count >= expectedNames.Length)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached
        }

        actualNames.Should().Equal(expectedNames, "User event names should match");
        
        for (int i = 0; i < Math.Min(actualPayloads.Count, expectedPayloads.Length); i++)
        {
            actualPayloads[i].Should().Equal(expectedPayloads[i],
                $"User event payload {i} should match");
        }
    }

    /// <summary>
    /// Verifies that expected queries occurred with specific names and payloads.
    /// </summary>
    public static async Task TestQueryEventsAsync(
        ChannelReader<Event> eventChannel,
        string[] expectedNames,
        byte[][] expectedPayloads,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        var actualNames = new List<string>();
        var actualPayloads = new List<byte[]>();

        using var cts = new CancellationTokenSource(timeout.Value);

        try
        {
            await foreach (var evt in eventChannel.ReadAllAsync(cts.Token))
            {
                if (evt is Query query)
                {
                    actualNames.Add(query.Name);
                    actualPayloads.Add(query.Payload);
                    
                    if (actualNames.Count >= expectedNames.Length)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached
        }

        actualNames.Should().Equal(expectedNames, "Query names should match");
        
        for (int i = 0; i < Math.Min(actualPayloads.Count, expectedPayloads.Length); i++)
        {
            actualPayloads[i].Should().Equal(expectedPayloads[i],
                $"Query payload {i} should match");
        }
    }

    /// <summary>
    /// Allocates a unique IP address for testing.
    /// Returns loopback addresses with incrementing last octet.
    /// </summary>
    public static IPAddress AllocateTestIP()
    {
        lock (_portLock)
        {
            // Use 127.0.x.x range for testing
            // Increment the port counter to get unique IPs
            var uniqueId = ++_portCounter;
            var octet = (uniqueId % 254) + 1;
            return IPAddress.Parse($"127.0.0.{octet}");
        }
    }

    /// <summary>
    /// Creates a test event channel for Serf configuration.
    /// Returns both reader and writer for test control.
    /// </summary>
    public static (ChannelWriter<Event> writer, ChannelReader<Event> reader) CreateTestEventChannel(
        int capacity = 1000)
    {
        var channel = Channel.CreateBounded<Event>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        return (channel.Writer, channel.Reader);
    }

    /// <summary>
    /// Waits for a specific condition to become true with polling.
    /// Useful for waiting on eventual consistency conditions.
    /// </summary>
    public static async Task WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string? errorMessage = null)
    {
        using var cts = new CancellationTokenSource(timeout);
        
        while (!cts.Token.IsCancellationRequested)
        {
            if (condition())
            {
                return;
            }

            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw new TimeoutException(errorMessage ?? "Condition not met within timeout");
    }

    /// <summary>
    /// Creates multiple test Serf configurations for cluster testing.
    /// Each gets a unique name and port.
    /// </summary>
    public static List<SerfConfig> CreateTestCluster(int nodeCount)
    {
        var configs = new List<SerfConfig>();
        
        for (int i = 0; i < nodeCount; i++)
        {
            configs.Add(CreateTestConfig($"node-{i}"));
        }

        return configs;
    }
}
