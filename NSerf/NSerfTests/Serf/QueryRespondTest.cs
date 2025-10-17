// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Tests for Query.RespondAsync() method
// Ported from: github.com/hashicorp/serf/serf/event.go and query tests

using FluentAssertions;
using NSerf.Serf;
using NSerf.Serf.Events;
using NSerf.Memberlist.Configuration;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for Query response functionality.
/// </summary>
public class QueryRespondTest
{
    /// <summary>
    /// Tests that RespondAsync validates query has a Serf instance.
    /// </summary>
    [Fact]
    public async Task RespondAsync_NoSerfInstance_ShouldThrow()
    {
        // Arrange
        var query = new Query
        {
            Name = "test-query",
            Payload = new byte[] { 1, 2, 3 },
            SerfInstance = null // No Serf instance
        };

        // Act & Assert
        var act = async () => await query.RespondAsync(new byte[] { 4, 5, 6 });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot respond to query without Serf instance");
    }

    /// <summary>
    /// Tests that RespondAsync validates response size limit.
    /// </summary>
    [Fact]
    public async Task RespondAsync_ResponseTooLarge_ShouldThrow()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            QueryResponseSizeLimit = 100, // Small limit
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        var query = new Query
        {
            Name = "test-query",
            Id = 123,
            Payload = new byte[] { 1, 2, 3 },
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10)
        };

        // Create a response that's too large
        var largeResponse = new byte[config.QueryResponseSizeLimit + 100];

        // Act & Assert
        var act = async () => await query.RespondAsync(largeResponse);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Response exceeds limit of {config.QueryResponseSizeLimit} bytes");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that RespondAsync fails if deadline has passed.
    /// </summary>
    [Fact]
    public async Task RespondAsync_PastDeadline_ShouldThrow()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        var query = new Query
        {
            Name = "test-query",
            Id = 123,
            Payload = new byte[] { 1, 2, 3 },
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "source-node",
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(-1) // Deadline in the past
        };

        // Act & Assert
        var act = async () => await query.RespondAsync(new byte[] { 4, 5, 6 });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Response is past the deadline");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that RespondAsync fails if already responded.
    /// </summary>
    [Fact]
    public async Task RespondAsync_AlreadyResponded_ShouldThrow()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        var query = new Query
        {
            Name = "test-query",
            Id = 123,
            Payload = new byte[] { 1, 2, 3 },
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "source-node",
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10)
        };

        // Act - Respond once
        await query.RespondAsync(new byte[] { 4, 5, 6 });

        // Act & Assert - Try to respond again
        var act = async () => await query.RespondAsync(new byte[] { 7, 8, 9 });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Response already sent");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that RespondAsync creates proper response message.
    /// </summary>
    [Fact]
    public async Task RespondAsync_ValidQuery_ShouldCreateResponse()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        var ltime = new LamportTime(42);
        var query = new Query
        {
            Name = "test-query",
            Id = 123,
            LTime = ltime,
            Payload = new byte[] { 1, 2, 3 },
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "source-node",
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10)
        };

        var responsePayload = new byte[] { 4, 5, 6 };

        // Act - Should not throw (even though sending is stubbed)
        await query.RespondAsync(responsePayload);

        // Assert - Deadline should be cleared
        query.GetDeadline().Should().Be(default(DateTime), "deadline should be cleared after response");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests Query.SourceNode() returns the source node name.
    /// </summary>
    [Fact]
    public void Query_SourceNode_ShouldReturnSourceNodeName()
    {
        // Arrange
        var query = new Query
        {
            SourceNodeName = "test-source-node"
        };

        // Act
        var sourceName = query.SourceNode();

        // Assert
        sourceName.Should().Be("test-source-node");
    }

    /// <summary>
    /// Tests Query.GetDeadline() returns the deadline.
    /// </summary>
    [Fact]
    public void Query_GetDeadline_ShouldReturnDeadline()
    {
        // Arrange
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var query = new Query
        {
            Deadline = deadline
        };

        // Act
        var result = query.GetDeadline();

        // Assert
        result.Should().Be(deadline);
    }

    /// <summary>
    /// Tests Query.EventType() returns Query event type.
    /// </summary>
    [Fact]
    public void Query_EventType_ShouldReturnQuery()
    {
        // Arrange
        var query = new Query();

        // Act
        var eventType = query.EventType();

        // Assert
        eventType.Should().Be(EventType.Query);
    }

    /// <summary>
    /// Tests Query.ToString() returns formatted string.
    /// </summary>
    [Fact]
    public void Query_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var query = new Query
        {
            Name = "my-test-query"
        };

        // Act
        var str = query.ToString();

        // Assert
        str.Should().Be("query: my-test-query");
    }
}
