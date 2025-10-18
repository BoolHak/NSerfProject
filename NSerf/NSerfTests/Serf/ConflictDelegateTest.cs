// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/conflict_delegate.go

using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;
using NSerf.Serf;
using System.Collections.Concurrent;
using System.Net;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for the Serf Conflict Delegate implementation.
/// Tests the bridge between Memberlist conflict detection and Serf handlers.
/// 
/// Note: Go has no dedicated unit tests for conflict_delegate.go.
/// These tests verify the ConflictDelegate correctly forwards to Serf conflict handler.
/// </summary>
public class ConflictDelegateTest
{
    /// <summary>
    /// Simple test logger that captures log messages for verification.
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
    [Fact]
    public void NotifyConflict_ShouldCallSerfHandleNodeConflict()
    {
        // Arrange
        var logger = new TestLogger();
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            Logger = logger
        };
        var serf = new NSerf.Serf.Serf(config);
        var conflictDelegate = new ConflictDelegate(serf);

        var existingNode = new Node
        {
            Name = "conflicted-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        var otherNode = new Node
        {
            Name = "conflicted-node", // Same name!
            Addr = IPAddress.Parse("127.0.0.2"),
            Port = 8001,
            Meta = Array.Empty<byte>()
        };

        // Act
        conflictDelegate.NotifyConflict(existingNode, otherNode);
        
        // Assert - Should log warning about conflict (not local node)
        var warningLogs = logger.Messages.Where(m => m.level == LogLevel.Warning).ToList();
        warningLogs.Should().ContainSingle("should log one warning about name conflict");
        
        var warningMessage = warningLogs[0].message;
        warningMessage.Should().Contain("conflicted-node", "should mention the conflicted node name");
        warningMessage.Should().Contain("127.0.0.1", "should mention first node address");
        warningMessage.Should().Contain("127.0.0.2", "should mention second node address");
    }

    [Fact]
    public void Constructor_WithNullSerf_ShouldThrow()
    {
        // Act & Assert
        var act = () => new ConflictDelegate(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serf");
    }

    [Fact]
    public void NotifyConflict_WithSameNameDifferentAddress_ShouldHandle()
    {
        // Arrange
        var logger = new TestLogger();
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            Logger = logger,
            EnableNameConflictResolution = false // Disable resolution for simple test
        };
        var serf = new NSerf.Serf.Serf(config);
        var conflictDelegate = new ConflictDelegate(serf);

        var existingNode = new Node
        {
            Name = "duplicate",
            Addr = IPAddress.Parse("10.0.0.1"),
            Port = 7946,
            Meta = new byte[] { 1, 2, 3 }
        };

        var otherNode = new Node
        {
            Name = "duplicate",
            Addr = IPAddress.Parse("10.0.0.2"),
            Port = 7946,
            Meta = new byte[] { 4, 5, 6 }
        };

        // Act
        conflictDelegate.NotifyConflict(existingNode, otherNode);

        // Assert - Should log warning (not error since it's not local node)
        var warningLogs = logger.Messages.Where(m => m.level == LogLevel.Warning).ToList();
        warningLogs.Should().ContainSingle("should log warning about duplicate name");
        
        var warningMessage = warningLogs[0].message;
        warningMessage.Should().Contain("duplicate", "should mention the duplicate node name");
        warningMessage.Should().Contain("10.0.0.1", "should mention first address");
        warningMessage.Should().Contain("10.0.0.2", "should mention second address");
    }

    [Fact]
    public void NotifyConflict_WithNullExisting_ShouldNotCrash()
    {
        // Arrange
        var logger = new TestLogger();
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            Logger = logger
        };
        var serf = new NSerf.Serf.Serf(config);
        var conflictDelegate = new ConflictDelegate(serf);

        var otherNode = new Node
        {
            Name = "node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        // Act
        var act = () => conflictDelegate.NotifyConflict(null!, otherNode);
        
        // Assert - Should handle null gracefully without throwing
        act.Should().NotThrow("Serf should handle null nodes gracefully");
        
        // Verify error or warning was logged for null input
        var errorOrWarningLogs = logger.Messages
            .Where(m => m.level == LogLevel.Error || m.level == LogLevel.Warning)
            .ToList();
        
        // Should either log error/warning or handle silently
        // (Implementation specific - Go logs warning for unexpected conditions)
        if (errorOrWarningLogs.Any())
        {
            errorOrWarningLogs.Should().Contain(m => 
                m.message.Contains("null") || m.message.Contains("conflict"),
                "should log about null node if logging occurs");
        }
    }

    [Fact]
    public void NotifyConflict_WithNullOther_ShouldNotCrash()
    {
        // Arrange
        var logger = new TestLogger();
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            Logger = logger
        };
        var serf = new NSerf.Serf.Serf(config);
        var conflictDelegate = new ConflictDelegate(serf);

        var existingNode = new Node
        {
            Name = "node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        // Act
        var act = () => conflictDelegate.NotifyConflict(existingNode, null!);
        
        // Assert - Should handle null gracefully without throwing
        act.Should().NotThrow("Serf should handle null nodes gracefully");
        
        // Verify error or warning was logged for null input
        var errorOrWarningLogs = logger.Messages
            .Where(m => m.level == LogLevel.Error || m.level == LogLevel.Warning)
            .ToList();
        
        // Should either log error/warning or handle silently
        if (errorOrWarningLogs.Any())
        {
            errorOrWarningLogs.Should().Contain(m => 
                m.message.Contains("null") || m.message.Contains("conflict"),
                "should log about null node if logging occurs");
        }
    }

    [Fact]
    public void NotifyConflict_MultipleConflicts_ShouldHandleSequentially()
    {
        // Arrange
        var logger = new TestLogger();
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            Logger = logger
        };
        var serf = new NSerf.Serf.Serf(config);
        var conflictDelegate = new ConflictDelegate(serf);

        var conflicts = new[]
        {
            (new Node { Name = "node1", Addr = IPAddress.Parse("10.0.0.1"), Port = 8000, Meta = Array.Empty<byte>() },
             new Node { Name = "node1", Addr = IPAddress.Parse("10.0.0.2"), Port = 8000, Meta = Array.Empty<byte>() }),
            
            (new Node { Name = "node2", Addr = IPAddress.Parse("10.0.0.3"), Port = 8000, Meta = Array.Empty<byte>() },
             new Node { Name = "node2", Addr = IPAddress.Parse("10.0.0.4"), Port = 8000, Meta = Array.Empty<byte>() })
        };

        // Act
        foreach (var (existing, other) in conflicts)
        {
            conflictDelegate.NotifyConflict(existing, other);
        }

        // Assert - Should log warning for each conflict
        var warningLogs = logger.Messages.Where(m => m.level == LogLevel.Warning).ToList();
        warningLogs.Should().HaveCount(2, "should log one warning per conflict");
        
        // Verify both conflicts were logged (ConcurrentBag doesn't preserve order)
        warningLogs.Should().Contain(m => m.message.Contains("node1"), "should log node1 conflict");
        warningLogs.Should().Contain(m => m.message.Contains("node2"), "should log node2 conflict");
        
        // Verify both conflicts contain their respective addresses
        var node1Log = warningLogs.First(m => m.message.Contains("node1"));
        node1Log.message.Should().Contain("10.0.0.1", "node1 conflict should mention first address");
        node1Log.message.Should().Contain("10.0.0.2", "node1 conflict should mention second address");
        
        var node2Log = warningLogs.First(m => m.message.Contains("node2"));
        node2Log.message.Should().Contain("10.0.0.3", "node2 conflict should mention first address");
        node2Log.message.Should().Contain("10.0.0.4", "node2 conflict should mention second address");
    }
}
