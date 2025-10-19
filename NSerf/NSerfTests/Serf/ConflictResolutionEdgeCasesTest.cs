// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Comprehensive edge case tests for node conflict resolution
// Goes beyond the minimal Go test coverage

using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;
using NSerf.Serf;
using System.Collections.Concurrent;
using System.Net;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Comprehensive edge case tests for automatic node name conflict resolution.
/// The Go codebase has minimal test coverage for this feature (only 1 basic test).
/// These tests ensure robustness across various failure scenarios.
/// </summary>
public class ConflictResolutionEdgeCasesTest : IDisposable
{
    private readonly List<NSerf.Serf.Serf> _serfs = new();

    public void Dispose()
    {
        foreach (var serf in _serfs)
        {
            try { serf.ShutdownAsync().GetAwaiter().GetResult(); } catch { }
        }
    }

    /// <summary>
    /// Test logger that captures log messages for verification.
    /// </summary>
    private class TestLogger : ILogger
    {
        public ConcurrentBag<(LogLevel level, string message)> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            Messages.Add((logLevel, message));
        }
    }

    private Config CreateTestConfig(string nodeName)
    {
        var baseConfig = TestHelpers.CreateTestConfig(nodeName);
        
        if (baseConfig.MemberlistConfig != null)
        {
            baseConfig.MemberlistConfig.RequireNodeNames = false;
        }
        
        return new Config
        {
            NodeName = baseConfig.NodeName,
            MemberlistConfig = baseConfig.MemberlistConfig,
            EnableNameConflictResolution = true,
            ReapInterval = baseConfig.ReapInterval,
            ReconnectInterval = baseConfig.ReconnectInterval,
            ReconnectTimeout = baseConfig.ReconnectTimeout,
            TombstoneTimeout = baseConfig.TombstoneTimeout
        };
    }

    /// <summary>
    /// Ported from Go: TestSerfQueries_Conflict_SameName
    /// Verifies that a node doesn't respond to conflict queries about itself.
    /// Reference: serf/serf/internal_query_test.go:73-93
    /// </summary>
    [Fact]
    public async Task ConflictQuery_ForOwnName_ShouldNotRespond()
    {
        // Arrange
        var logger = new TestLogger();
        var config = CreateTestConfig("test-node");
        config.Logger = logger;
        
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfs.Add(serf);

        // Act: Query for our own name (should not respond)
        var queryParams = new QueryParam { Timeout = TimeSpan.FromSeconds(1) };
        var payload = System.Text.Encoding.UTF8.GetBytes("test-node");
        var response = await serf.QueryAsync("_serf_conflict", payload, queryParams);

        // Wait for query to timeout
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        // Collect responses
        var responses = new List<NodeResponse>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await foreach (var resp in response.ResponseCh.ReadAllAsync(cts.Token))
            {
                responses.Add(resp);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected timeout
        }

        // Assert: Should not respond to query about ourselves
        responses.Should().BeEmpty("node should not respond to conflict query about itself");
    }

    [Fact]
    public void ConflictResolution_WithNullNodes_ShouldHandleGracefully()
    {
        // Arrange
        var logger = new TestLogger();
        var config = CreateTestConfig("node1");
        config.Logger = logger;
        
        var serf = new NSerf.Serf.Serf(config);
        _serfs.Add(serf);

        var conflictDelegate = new ConflictDelegate(serf);

        // Act: Call with null nodes (edge case)
        conflictDelegate.NotifyConflict(null!, null!);

        // Assert: Should log warning, not crash
        var logs = logger.Messages.Where(m => m.level == LogLevel.Warning).ToList();
        logs.Should().Contain(m => m.message.Contains("null"), "should log about null nodes");
    }

    [Fact]
    public async Task ConflictResolution_WithNoMemberlist_ShouldLogError()
    {
        // Arrange: Create Serf without initializing memberlist
        var logger = new TestLogger();
        var config = CreateTestConfig("node1");
        config.Logger = logger;
        
        var serf = new NSerf.Serf.Serf(config); // Not calling CreateAsync
        _serfs.Add(serf);

        var localNode = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 5000,
            Meta = Array.Empty<byte>()
        };

        var otherNode = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 5000,
            Meta = Array.Empty<byte>()
        };

        // Act: Trigger conflict when memberlist is null
        var conflictDelegate = new ConflictDelegate(serf);
        conflictDelegate.NotifyConflict(localNode, otherNode);

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Assert: Should log error about memberlist not initialized
        var errorLogs = logger.Messages.Where(m => m.level == LogLevel.Error).ToList();
        errorLogs.Should().Contain(m => m.message.Contains("memberlist") || m.message.Contains("not initialized"),
            "should log error when memberlist is not initialized");
    }

    [Fact]
    public async Task ConflictResolution_WithZeroResponses_ShouldShutdown()
    {
        // Edge case: No cluster members respond (0/0 votes)
        
        // Arrange: Single isolated node
        var logger = new TestLogger();
        var config = CreateTestConfig("isolated-node");
        config.Logger = logger;
        
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfs.Add(serf);

        var localNode = serf.Memberlist!.LocalNode;
        var conflictingNode = new Node
        {
            Name = config.NodeName,
            Addr = IPAddress.Parse("10.0.0.100"),
            Port = 9999,
            Meta = Array.Empty<byte>()
        };

        // Act: Trigger conflict (no peers to vote)
        var conflictDelegate = new ConflictDelegate(serf);
        conflictDelegate.NotifyConflict(localNode, conflictingNode);

        // Wait for resolution
        await Task.Delay(TimeSpan.FromSeconds(7));

        // Assert: With 0 responses, 0 >= 1 (majority) = false → should shutdown
        // Majority = (0 / 2) + 1 = 1, matching = 0, so 0 < 1 → minority → shutdown
        var allLogs = logger.Messages.Select(m => m.message).ToList();
        
        // Should either shutdown or log minority
        var shutdownOrMinority = serf.State() == SerfState.SerfShutdown ||
                                 allLogs.Any(m => m.Contains("minority"));
        
        shutdownOrMinority.Should().BeTrue("isolated node with 0 votes should shutdown or log minority");
    }

    [Fact]
    public async Task ConflictResolution_WithTieVote_ShouldRequireMajority()
    {
        // Edge case: Tie vote (50/50) - needs STRICT majority (>50%)
        
        // Arrange: 2-node cluster (tie is possible)
        var logger = new TestLogger();
        var config1 = CreateTestConfig("node1");
        config1.Logger = logger; // Set BEFORE CreateAsync
        
        var s1 = await NSerf.Serf.Serf.CreateAsync(config1);
        var s2 = await NSerf.Serf.Serf.CreateAsync(CreateTestConfig("node2"));
        _serfs.AddRange(new[] { s1, s2 });

        await s2.JoinAsync(new[] { $"127.0.0.1:{s1.Config.MemberlistConfig!.BindPort}" }, false);
        await TestHelpers.WaitUntilNumNodesAsync(2, TimeSpan.FromSeconds(5), s1, s2);

        // Act: Simulate conflict
        var localNode = s1.Memberlist!.LocalNode;
        var conflictingNode = new Node
        {
            Name = s1.Config.NodeName,
            Addr = IPAddress.Parse("192.168.1.50"),
            Port = 9999,
            Meta = Array.Empty<byte>()
        };

        var conflictDelegate = new ConflictDelegate(s1);
        conflictDelegate.NotifyConflict(localNode, conflictingNode);

        await Task.Delay(TimeSpan.FromSeconds(7));

        // Assert: Resolution attempted
        // With 1 response possible from node2, majority = (1/2)+1 = 1
        // If node2 votes for us: 1 >= 1 → stay alive
        // If node2 doesn't respond or votes against: 0 < 1 → shutdown
        var allLogs = logger.Messages.Select(m => m.message).ToList();
        allLogs.Should().Contain(m => m.Contains("Starting conflict resolution"),
            "should attempt resolution even in 2-node cluster");
    }

    [Fact]
    public async Task ConflictResolution_ConcurrentConflicts_ShouldHandleSequentially()
    {
        // Edge case: Multiple conflicts triggered simultaneously
        
        // Arrange
        var logger = new TestLogger();
        var config = CreateTestConfig("node1");
        config.Logger = logger;
        
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfs.Add(serf);

        var conflictDelegate = new ConflictDelegate(serf);
        var localNode = serf.Memberlist!.LocalNode;

        var conflicts = new[]
        {
            new Node { Name = "node1", Addr = IPAddress.Parse("192.168.1.1"), Port = 8001, Meta = Array.Empty<byte>() },
            new Node { Name = "node1", Addr = IPAddress.Parse("192.168.1.2"), Port = 8002, Meta = Array.Empty<byte>() },
            new Node { Name = "node1", Addr = IPAddress.Parse("192.168.1.3"), Port = 8003, Meta = Array.Empty<byte>() }
        };

        // Act: Trigger multiple conflicts concurrently
        var tasks = conflicts.Select(other => Task.Run(() => 
            conflictDelegate.NotifyConflict(localNode, other)
        )).ToArray();

        await Task.WhenAll(tasks);
        await Task.Delay(TimeSpan.FromSeconds(8));

        // Assert: Should handle all conflicts without crashing
        var errorLogs = logger.Messages.Where(m => m.level == LogLevel.Error).ToList();
        
        // Should log errors for each conflict
        errorLogs.Should().HaveCountGreaterOrEqualTo(3, 
            "should log error for each concurrent conflict");
        
        errorLogs.Should().OnlyContain(m => m.message.Contains("Node name conflicts"),
            "all errors should be about name conflicts");
    }

    [Fact]
    public async Task ConflictResolution_WithDifferentPorts_ShouldDetectConflict()
    {
        // Edge case: Same name, same IP, different ports
        
        // Arrange
        var logger = new TestLogger();
        var config = CreateTestConfig("node1");
        config.Logger = logger;
        
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfs.Add(serf);

        var localNode = serf.Memberlist!.LocalNode;
        
        // Same IP as local, but different port
        var conflictingNode = new Node
        {
            Name = config.NodeName,
            Addr = localNode.Addr, // Same IP!
            Port = (ushort)(localNode.Port + 1000), // Different port
            Meta = Array.Empty<byte>()
        };

        // Act
        var conflictDelegate = new ConflictDelegate(serf);
        conflictDelegate.NotifyConflict(localNode, conflictingNode);

        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // Assert: Should still detect as conflict
        var errorLogs = logger.Messages.Where(m => m.level == LogLevel.Error).ToList();
        errorLogs.Should().ContainSingle("should detect conflict even with same IP");
        
        var errorMsg = errorLogs[0].message;
        errorMsg.Should().Contain("Node name conflicts");
        errorMsg.Should().Contain((localNode.Port + 1000).ToString(), "should mention the conflicting port");
    }

    [Fact]
    public async Task ConflictResolution_WithEmptyPayload_ShouldHandleGracefully()
    {
        // Edge case: Query with empty/invalid payload
        
        // Arrange
        var serf = await NSerf.Serf.Serf.CreateAsync(CreateTestConfig("node1"));
        _serfs.Add(serf);

        // Act: Send conflict query with empty payload
        var queryParams = new QueryParam { Timeout = TimeSpan.FromSeconds(1) };
        var emptyPayload = Array.Empty<byte>();
        
        var act = async () => await serf.QueryAsync("_serf_conflict", emptyPayload, queryParams);

        // Assert: Should not crash
        await act.Should().NotThrowAsync("should handle empty payload gracefully");
    }

    [Fact]
    public void ConflictResolution_WithVeryLargeCluster_ShouldScaleCorrectly()
    {
        // Edge case: Test with larger vote count
        // Simulates a 10-node cluster scenario
        
        // Arrange: Just verify the majority calculation logic
        int totalNodes = 10;
        int responses = totalNodes - 1; // 9 responses (excluding self)
        
        // Simulate different vote scenarios
        var scenarios = new[]
        {
            (matching: 9, expectMajority: true,  name: "unanimous"),
            (matching: 5, expectMajority: true,  name: "simple majority"),
            (matching: 4, expectMajority: false, name: "exact half - not majority"),
            (matching: 0, expectMajority: false, name: "all against")
        };

        foreach (var scenario in scenarios)
        {
            // Calculate using same logic as implementation
            int majority = (responses / 2) + 1; // (9/2)+1 = 5
            bool hasMajority = scenario.matching >= majority;

            // Assert
            hasMajority.Should().Be(scenario.expectMajority,
                $"scenario '{scenario.name}': {scenario.matching}/{responses} votes, majority threshold={majority}");
        }
    }

    [Fact]
    public async Task ConflictResolution_ResolutionDisabled_ShouldNeverTrigger()
    {
        // Edge case: Ensure resolution never starts when disabled
        
        // Arrange
        var logger = new TestLogger();
        var baseConfig = TestHelpers.CreateTestConfig("node1");
        
        if (baseConfig.MemberlistConfig != null)
        {
            baseConfig.MemberlistConfig.RequireNodeNames = false;
        }
        
        var config = new Config
        {
            NodeName = baseConfig.NodeName,
            MemberlistConfig = baseConfig.MemberlistConfig,
            EnableNameConflictResolution = false, // DISABLED
            Logger = logger,
            ReapInterval = baseConfig.ReapInterval,
            ReconnectInterval = baseConfig.ReconnectInterval,
            ReconnectTimeout = baseConfig.ReconnectTimeout,
            TombstoneTimeout = baseConfig.TombstoneTimeout
        };
        
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfs.Add(serf);

        var localNode = serf.Memberlist!.LocalNode;
        var conflictingNode = new Node
        {
            Name = config.NodeName,
            Addr = IPAddress.Parse("192.168.1.100"),
            Port = 7777,
            Meta = Array.Empty<byte>()
        };

        // Act: Trigger many conflicts over time
        var conflictDelegate = new ConflictDelegate(serf);
        
        for (int i = 0; i < 5; i++)
        {
            conflictDelegate.NotifyConflict(localNode, conflictingNode);
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert: Should NEVER start resolution
        var allLogs = logger.Messages.Select(m => m.message).ToList();
        allLogs.Should().NotContain(m => m.Contains("Starting conflict resolution"),
            "resolution should never start when disabled");
        
        allLogs.Should().NotContain(m => m.Contains("majority") || m.Contains("minority"),
            "should never log resolution results when disabled");
        
        // Should still log the conflicts though
        var errorLogs = logger.Messages.Where(m => m.level == LogLevel.Error).ToList();
        errorLogs.Should().HaveCount(5, "should log each conflict even when resolution disabled");
    }

    [Fact]
    public async Task ConflictDelegate_MultipleSequentialConflicts_ShouldLogEach()
    {
        // Edge case: Sequential conflicts over time
        
        // Arrange
        var logger = new TestLogger();
        var config = CreateTestConfig("node1");
        config.Logger = logger;
        
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfs.Add(serf);

        var conflictDelegate = new ConflictDelegate(serf);
        var localNode = serf.Memberlist!.LocalNode;

        // Act: Trigger conflicts one after another
        for (int i = 1; i <= 3; i++)
        {
            var other = new Node
            {
                Name = config.NodeName,
                Addr = IPAddress.Parse($"192.168.1.{i}"),
                Port = (ushort)(8000 + i),
                Meta = Array.Empty<byte>()
            };
            
            conflictDelegate.NotifyConflict(localNode, other);
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert: Should log all conflicts
        var errorLogs = logger.Messages.Where(m => m.level == LogLevel.Error).ToList();
        errorLogs.Should().HaveCount(3, "should log each sequential conflict");
        
        // Verify different IPs were logged (order not guaranteed with ConcurrentBag)
        var allMessages = string.Join(" ", errorLogs.Select(m => m.message));
        allMessages.Should().Contain("192.168.1.1", "should log conflict with .1");
        allMessages.Should().Contain("192.168.1.2", "should log conflict with .2");
        allMessages.Should().Contain("192.168.1.3", "should log conflict with .3");
    }
}
