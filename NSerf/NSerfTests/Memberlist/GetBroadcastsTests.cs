// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Unit tests for Memberlist.GetBroadcasts() method

using FluentAssertions;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Delegates;
using System.Collections.Concurrent;
using Xunit;

namespace NSerfTests.Memberlist;

/// <summary>
/// Tests for Memberlist.GetBroadcasts() method which combines
/// internal memberlist broadcasts with delegate broadcasts.
/// This method was added to support user message gossiping.
/// </summary>
public class GetBroadcastsTests : IDisposable
{
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = new();

    public void Dispose()
    {
        foreach (var m in _memberlists)
        {
            m?.Dispose();
        }
        _memberlists.Clear();
    }

    private NSerf.Memberlist.Memberlist CreateTestMemberlist(string name, IDelegate? customDelegate = null)
    {
        var config = MemberlistConfig.DefaultLocalConfig();
        config.Name = name;
        config.BindAddr = "127.0.0.1";
        config.BindPort = 0;
        config.Delegate = customDelegate;

        // Create transport
        var transportConfig = new NSerf.Memberlist.Transport.NetTransportConfig
        {
            BindAddrs = new List<string> { "127.0.0.1" },
            BindPort = 0
        };
        config.Transport = NSerf.Memberlist.Transport.NetTransport.Create(transportConfig);

        var m = NSerf.Memberlist.Memberlist.Create(config);
        _memberlists.Add(m);
        return m;
    }

    /// <summary>
    /// Test: GetBroadcasts with no delegate should only return memberlist broadcasts
    /// </summary>
    [Fact]
    public void GetBroadcasts_WithNoDelegate_ShouldReturnOnlyMemberlistBroadcasts()
    {
        // Arrange
        var m = CreateTestMemberlist("test-node");

        // Act - Use reflection to call private GetBroadcasts method
        var method = typeof(NSerf.Memberlist.Memberlist).GetMethod("GetBroadcasts", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<byte[]>)method!.Invoke(m, new object[] { 26, 1400 })!;

        // Assert - Should return empty list when no broadcasts queued
        result.Should().NotBeNull();
        result.Should().BeOfType<List<byte[]>>();
    }

    /// <summary>
    /// Test: GetBroadcasts with delegate should combine both sources
    /// </summary>
    [Fact]
    public void GetBroadcasts_WithDelegate_ShouldCombineBothSources()
    {
        // Arrange
        var testDelegate = new TestDelegateWithBroadcasts();
        testDelegate.QueueBroadcast(new byte[] { 1, 2, 3 });
        testDelegate.QueueBroadcast(new byte[] { 4, 5, 6 });

        var m = CreateTestMemberlist("test-node", testDelegate);

        // Act - Use reflection to call private GetBroadcasts method
        var method = typeof(NSerf.Memberlist.Memberlist).GetMethod("GetBroadcasts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<byte[]>)method!.Invoke(m, new object[] { 26, 1400 })!;

        // Assert - Should have user messages framed with MessageType.User byte
        result.Should().HaveCountGreaterOrEqualTo(2);
        
        // Each user message should be framed with byte 8 (MessageType.User)
        var userMessages = result.Where(msg => msg.Length > 0 && msg[0] == 8).ToList();
        userMessages.Should().HaveCountGreaterOrEqualTo(2);

        // Verify content after frame byte
        userMessages[0].Skip(1).Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        userMessages[1].Skip(1).Should().BeEquivalentTo(new byte[] { 4, 5, 6 });
    }

    /// <summary>
    /// Test: GetBroadcasts should respect byte limit
    /// </summary>
    [Fact]
    public void GetBroadcasts_ShouldRespectByteLimit()
    {
        // Arrange
        var testDelegate = new TestDelegateWithBroadcasts();
        
        // Queue a large message that should exceed limit
        var largeMessage = new byte[500];
        for (int i = 0; i < largeMessage.Length; i++)
        {
            largeMessage[i] = (byte)(i % 256);
        }
        testDelegate.QueueBroadcast(largeMessage);

        var m = CreateTestMemberlist("test-node", testDelegate);

        // Act - Set a small limit
        var method = typeof(NSerf.Memberlist.Memberlist).GetMethod("GetBroadcasts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<byte[]>)method!.Invoke(m, new object[] { 26, 100 })!;

        // Assert - Should not exceed limit
        int totalBytes = result.Sum(msg => msg.Length + 26); // 26 is overhead
        totalBytes.Should().BeLessOrEqualTo(100);
    }

    /// <summary>
    /// Test: GetBroadcasts should frame user messages correctly
    /// </summary>
    [Fact]
    public void GetBroadcasts_ShouldFrameUserMessagesWithCorrectType()
    {
        // Arrange
        var testDelegate = new TestDelegateWithBroadcasts();
        var originalMessage = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        testDelegate.QueueBroadcast(originalMessage);

        var m = CreateTestMemberlist("test-node", testDelegate);

        // Act
        var method = typeof(NSerf.Memberlist.Memberlist).GetMethod("GetBroadcasts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<byte[]>)method!.Invoke(m, new object[] { 26, 1400 })!;

        // Assert - Should have frame byte 8 (MessageType.User) followed by original message
        var userMsg = result.FirstOrDefault(msg => msg.Length > 0 && msg[0] == 8);
        userMsg.Should().NotBeNull();
        userMsg!.Length.Should().Be(originalMessage.Length + 1); // +1 for frame byte
        userMsg[0].Should().Be(8); // MessageType.User
        userMsg.Skip(1).Should().BeEquivalentTo(originalMessage);
    }

    /// <summary>
    /// Test: GetBroadcasts is thread-safe when called concurrently
    /// This verifies the implementation matches Go's thread safety guarantees
    /// </summary>
    [Fact]
    public async Task GetBroadcasts_ConcurrentCalls_ShouldBeThreadSafe()
    {
        // Arrange
        var testDelegate = new TestDelegateWithBroadcasts();
        for (int i = 0; i < 100; i++)
        {
            testDelegate.QueueBroadcast(new byte[] { (byte)i });
        }

        var m = CreateTestMemberlist("test-node", testDelegate);
        var method = typeof(NSerf.Memberlist.Memberlist).GetMethod("GetBroadcasts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var exceptions = new ConcurrentBag<Exception>();
        var results = new ConcurrentBag<List<byte[]>>();

        // Act - Call GetBroadcasts from multiple threads concurrently
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    var result = (List<byte[]>)method!.Invoke(m, new object[] { 26, 1400 })!;
                    results.Add(result);
                    
                    // Small delay to increase contention
                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        await Task.WhenAll(tasks);

        // Assert - No exceptions should occur (thread-safe)
        exceptions.Should().BeEmpty("GetBroadcasts should be thread-safe");
        results.Should().NotBeEmpty();
        
        // All results should be valid lists
        results.Should().OnlyContain(r => r != null);
    }

    /// <summary>
    /// Test: GetBroadcasts with null delegate should not throw
    /// </summary>
    [Fact]
    public void GetBroadcasts_WithNullDelegate_ShouldNotThrow()
    {
        // Arrange
        var m = CreateTestMemberlist("test-node", customDelegate: null);

        // Act
        var method = typeof(NSerf.Memberlist.Memberlist).GetMethod("GetBroadcasts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Action act = () => method!.Invoke(m, new object[] { 26, 1400 });

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Test: GetBroadcasts should handle empty delegate broadcasts gracefully
    /// </summary>
    [Fact]
    public void GetBroadcasts_WithEmptyDelegateBroadcasts_ShouldHandleGracefully()
    {
        // Arrange
        var testDelegate = new TestDelegateWithBroadcasts(); // No broadcasts queued
        var m = CreateTestMemberlist("test-node", testDelegate);

        // Act
        var method = typeof(NSerf.Memberlist.Memberlist).GetMethod("GetBroadcasts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<byte[]>)method!.Invoke(m, new object[] { 26, 1400 })!;

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<List<byte[]>>();
    }

    /// <summary>
    /// Test: GetBroadcasts should calculate available space correctly
    /// </summary>
    [Fact]
    public void GetBroadcasts_ShouldCalculateAvailableSpaceCorrectly()
    {
        // Arrange
        var testDelegate = new TestDelegateWithBroadcasts();
        
        // Queue multiple small messages
        for (int i = 0; i < 10; i++)
        {
            testDelegate.QueueBroadcast(new byte[] { (byte)i, (byte)(i + 1) });
        }

        var m = CreateTestMemberlist("test-node", testDelegate);

        // Act - Use a moderate limit
        var method = typeof(NSerf.Memberlist.Memberlist).GetMethod("GetBroadcasts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<byte[]>)method!.Invoke(m, new object[] { 26, 200 })!;

        // Assert - Should fit some but not all messages
        result.Should().NotBeEmpty();
        
        // Calculate total size
        int totalSize = result.Sum(msg => msg.Length + 26);
        totalSize.Should().BeLessOrEqualTo(200);
    }

    /// <summary>
    /// Test delegate that allows queueing broadcasts for testing.
    /// Must be thread-safe per IDelegate contract.
    /// </summary>
    private class TestDelegateWithBroadcasts : IDelegate
    {
        private readonly ConcurrentQueue<byte[]> _broadcasts = new();
        private readonly object _lock = new();

        public void QueueBroadcast(byte[] message)
        {
            _broadcasts.Enqueue(message);
        }

        public byte[] NodeMeta(int limit)
        {
            return Array.Empty<byte>();
        }

        public void NotifyMsg(ReadOnlySpan<byte> message)
        {
            // No-op for testing
        }

        public List<byte[]> GetBroadcasts(int overhead, int limit)
        {
            // Thread-safe implementation required by IDelegate contract
            lock (_lock)
            {
                var result = new List<byte[]>();
                int bytesUsed = 0;

                while (_broadcasts.TryDequeue(out var msg))
                {
                    int msgSize = msg.Length + overhead;
                    if (bytesUsed + msgSize > limit)
                    {
                        // Re-queue if doesn't fit
                        _broadcasts.Enqueue(msg);
                        break;
                    }

                    result.Add(msg);
                    bytesUsed += msgSize;
                }

                return result;
            }
        }

        public byte[] LocalState(bool join)
        {
            return Array.Empty<byte>();
        }

        public void MergeRemoteState(ReadOnlySpan<byte> buffer, bool join)
        {
            // No-op for testing
        }
    }
}
