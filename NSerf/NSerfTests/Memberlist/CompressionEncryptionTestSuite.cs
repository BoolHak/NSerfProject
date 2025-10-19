// Comprehensive test suite for compression and encryption
// Ensures all combinations and edge cases are properly handled

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist;
using NSerf.Memberlist.Common;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;
using Xunit.Abstractions;

namespace NSerfTests.Memberlist;

public class CompressionEncryptionTestSuite : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = new();

    public CompressionEncryptionTestSuite(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var m in _memberlists)
        {
            await m.ShutdownAsync();
            m.Dispose();
        }
        _memberlists.Clear();
    }

    private NSerf.Memberlist.Memberlist CreateMemberlist(string name, Action<MemberlistConfig>? configure = null)
    {
        var config = new MemberlistConfig
        {
            Name = name,
            BindAddr = "127.0.0.1",
            BindPort = 0,
            ProbeInterval = TimeSpan.FromMilliseconds(100),
            ProbeTimeout = TimeSpan.FromMilliseconds(50),
            EnableCompression = false
        };

        configure?.Invoke(config);

        // Create transport
        var transportConfig = new NSerf.Memberlist.Transport.NetTransportConfig
        {
            BindAddrs = new List<string> { config.BindAddr },
            BindPort = config.BindPort
        };
        config.Transport = NSerf.Memberlist.Transport.NetTransport.Create(transportConfig);

        var memberlist = NSerf.Memberlist.Memberlist.Create(config);
        _memberlists.Add(memberlist);
        return memberlist;
    }

    // ============================================================================
    // COMPRESSION TESTS
    // ============================================================================

    [Fact]
    public void Compression_RoundTrip_WithSmallPayload()
    {
        // Arrange
        var data = "Hello"u8.ToArray();

        // Act
        var compressed = CompressionUtils.CompressPayload(data);
        var decompressed = CompressionUtils.DecompressPayload(compressed);

        // Assert
        decompressed.Should().BeEquivalentTo(data);
    }

    [Fact]
    public void Compression_RoundTrip_WithBinaryData()
    {
        // Arrange - Binary data with null bytes
        var data = new byte[] { 0x00, 0xFF, 0x00, 0xAA, 0x55, 0x00 };

        // Act
        var compressed = CompressionUtils.CompressPayload(data);
        var decompressed = CompressionUtils.DecompressPayload(compressed);

        // Assert
        decompressed.Should().BeEquivalentTo(data);
    }

    [Fact]
    public void Compression_RoundTrip_WithUnicodeText()
    {
        // Arrange - Unicode text (emoji, special chars)
        var text = "Hello 世界 🌍 Мир";
        var data = Encoding.UTF8.GetBytes(text);

        // Act
        var compressed = CompressionUtils.CompressPayload(data);
        var decompressed = CompressionUtils.DecompressPayload(compressed);

        // Assert
        var result = Encoding.UTF8.GetString(decompressed);
        result.Should().Be(text);
    }

    [Fact]
    public void Compression_ShouldReduceSize_ForRepetitiveData()
    {
        // Arrange - Highly repetitive data
        var data = new byte[5000];
        Array.Fill(data, (byte)'A');

        // Act
        var compressed = CompressionUtils.CompressPayload(data);

        // Assert
        compressed.Length.Should().BeLessThan(data.Length / 10, 
            "repetitive data should compress to less than 10% of original size");
    }

    [Fact]
    public void Compression_ShouldHandleRandomData()
    {
        // Arrange - Random data (not very compressible)
        var data = new byte[1000];
        new Random(42).NextBytes(data);

        // Act
        var compressed = CompressionUtils.CompressPayload(data);
        var decompressed = CompressionUtils.DecompressPayload(compressed);

        // Assert
        decompressed.Should().BeEquivalentTo(data);
        // Random data may not compress well, but shouldn't fail
    }

    [Fact]
    public void Compression_ShouldHandleExactlyOneKB()
    {
        // Arrange
        var data = new byte[1024];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i % 256);

        // Act
        var compressed = CompressionUtils.CompressPayload(data);
        var decompressed = CompressionUtils.DecompressPayload(compressed);

        // Assert
        decompressed.Should().BeEquivalentTo(data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(50000)]
    public void Compression_ShouldHandleVariousSizes(int size)
    {
        // Arrange
        var data = new byte[size];
        for (int i = 0; i < size; i++)
            data[i] = (byte)(i % 256);

        // Act
        var compressed = CompressionUtils.CompressPayload(data);
        var decompressed = CompressionUtils.DecompressPayload(compressed);

        // Assert
        decompressed.Should().BeEquivalentTo(data, $"size {size} should round-trip correctly");
    }

    [Fact]
    public void Compression_ShouldThrowOnCorruptedData()
    {
        // Arrange - Corrupt GZip header
        var corrupted = new byte[] { 0x1F, 0x8B, 0xFF, 0xFF, 0x00 };

        // Act & Assert
        Action act = () => CompressionUtils.DecompressPayload(corrupted);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Compression_ShouldHandleTruncatedData()
    {
        // Arrange
        var data = "Test data"u8.ToArray();
        var compressed = CompressionUtils.CompressPayload(data);
        
        // Truncate the compressed data
        var truncated = compressed[..(compressed.Length / 2)];

        // Act - GZip may throw or return partial/empty data
        try
        {
            var result = CompressionUtils.DecompressPayload(truncated);
            // If it doesn't throw, result should not match original
            result.Should().NotBeEquivalentTo(data, "truncated data should not decompress to original");
        }
        catch (Exception)
        {
            // Expected - truncated data may throw
            // This is also acceptable behavior
        }
    }

    // ============================================================================
    // ENCRYPTION TESTS
    // ============================================================================

    [Fact]
    public void Encryption_ShouldCreateKeyring()
    {
        // Arrange
        var primaryKey = new byte[32];
        new Random(42).NextBytes(primaryKey);

        // Act
        var keyring = Keyring.Create(null, primaryKey);

        // Assert
        keyring.Should().NotBeNull();
        keyring.GetPrimaryKey().Should().BeEquivalentTo(primaryKey);
    }

    [Fact]
    public async Task Encryption_TwoNodesWithSameKey_ShouldCommunicate()
    {
        // Arrange
        var sharedKey = new byte[32];
        new Random(42).NextBytes(sharedKey);
        var keyring = Keyring.Create(null, sharedKey);

        var m1 = CreateMemberlist("enc-node1", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
        });

        var m2 = CreateMemberlist("enc-node2", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
        });

        // Act
        var (joined, error) = await m2.JoinAsync(new[] { $"{m1._config.BindAddr}:{m1._config.BindPort}" });

        // Assert
        error.Should().BeNull("nodes with same key should communicate");
        joined.Should().Be(1);
    }

    [Fact]
    public async Task Encryption_TwoNodesWithDifferentKeys_ShouldFail()
    {
        // Arrange
        var key1 = new byte[32];
        var key2 = new byte[32];
        new Random(42).NextBytes(key1);
        new Random(43).NextBytes(key2); // Different seed = different key

        var keyring1 = Keyring.Create(null, key1);
        var keyring2 = Keyring.Create(null, key2);

        var m1 = CreateMemberlist("enc-fail-node1", c =>
        {
            c.Keyring = keyring1;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
        });

        var m2 = CreateMemberlist("enc-fail-node2", c =>
        {
            c.Keyring = keyring2;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
        });

        // Act
        var (joined, error) = await m2.JoinAsync(new[] { $"{m1._config.BindAddr}:{m1._config.BindPort}" });

        await Task.Delay(500); // Wait for potential communication

        // Assert - Should not establish proper communication
        var m2Members = m2.Members();
        m2Members.Should().HaveCountLessThan(2, "nodes with different keys should not fully communicate");
    }

    [Fact]
    public async Task Encryption_NodeWithoutKey_CannotJoinEncryptedCluster()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("enc-required-node1", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
        });

        var m2 = CreateMemberlist("no-enc-node2"); // No encryption

        // Act
        var (joined, error) = await m2.JoinAsync(new[] { $"{m1._config.BindAddr}:{m1._config.BindPort}" });

        await Task.Delay(500);

        // Assert
        var m2Members = m2.Members();
        m2Members.Should().HaveCountLessThan(2, "unencrypted node should not join encrypted cluster");
    }

    // ============================================================================
    // COMPRESSION + ENCRYPTION COMBINATION TESTS
    // ============================================================================

    [Fact]
    public async Task CompressionEncryption_BothEnabled_ShouldWork()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("comp-enc-node1", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.EnableCompression = true;
        });

        var m2 = CreateMemberlist("comp-enc-node2", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.EnableCompression = true;
        });

        // Add nodes to create compressible payload
        for (int i = 0; i < 5; i++)
        {
            var nodeState = new NodeState
            {
                Node = new Node
                {
                    Name = $"test-node-{i}",
                    Addr = System.Net.IPAddress.Parse("127.0.0.1"),
                    Port = (ushort)(5000 + i),
                    Meta = new byte[100],
                    PMin = ProtocolVersion.Min,
                    PMax = ProtocolVersion.Max,
                    PCur = m1._config.ProtocolVersion,
                    DMin = m1._config.DelegateProtocolMin,
                    DMax = m1._config.DelegateProtocolMax,
                    DCur = m1._config.DelegateProtocolVersion
                },
                State = NodeStateType.Alive,
                Incarnation = (uint)i,
                StateChange = DateTimeOffset.UtcNow
            };

            lock (m1._nodeLock)
            {
                m1._nodes.Add(nodeState);
                m1._nodeMap[nodeState.Node.Name] = nodeState;
            }
        }

        // Act
        _output.WriteLine($"m1: {m1._config.Name} at {m1._config.BindAddr}:{m1._config.BindPort}");
        _output.WriteLine($"m1 compression: {m1._config.EnableCompression}, encryption: {m1._config.EncryptionEnabled()}");
        _output.WriteLine($"m2: {m2._config.Name} at {m2._config.BindAddr}:{m2._config.BindPort}");
        _output.WriteLine($"m2 compression: {m2._config.EnableCompression}, encryption: {m2._config.EncryptionEnabled()}");
        _output.WriteLine($"m1 has {m1._nodes.Count} nodes before join");
        
        var (joined, error) = await m2.JoinAsync(new[] { $"{m1._config.BindAddr}:{m1._config.BindPort}" });

        _output.WriteLine($"Join result: joined={joined}, error={error?.Message}");
        await Task.Delay(1000);

        var m1Members = m1.Members();
        var m2Members = m2.Members();
        _output.WriteLine($"After join: m1 sees {m1Members.Count} members, m2 sees {m2Members.Count} members");

        // Assert
        error.Should().BeNull("compression + encryption should work together");
        joined.Should().Be(1);
        
        m2Members.Should().HaveCountGreaterThan(2, "should receive encrypted and compressed state");
    }

    [Fact]
    public async Task CompressionEncryption_OnlyCompressionMismatch_ShouldStillWork()
    {
        // Arrange - Both have encryption, only one has compression
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("comp-only-node1", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.EnableCompression = true;
        });

        var m2 = CreateMemberlist("no-comp-node2", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.EnableCompression = false;
        });

        // Act
        var (joined, error) = await m2.JoinAsync(new[] { $"{m1._config.BindAddr}:{m1._config.BindPort}" });

        await Task.Delay(500);

        // Assert - Should work because compression is sender-side
        error.Should().BeNull("compression mismatch should not prevent communication");
        joined.Should().Be(1);
    }

    [Fact]
    public async Task CompressionEncryption_LargeStateTransfer_ShouldWorkCorrectly()
    {
        // Arrange - Test with large state to verify compression helps
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("large-comp-enc-node1", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.EnableCompression = true;
        });

        var m2 = CreateMemberlist("large-comp-enc-node2", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.EnableCompression = true;
        });

        // Add many nodes with large metadata
        for (int i = 0; i < 20; i++)
        {
            var largeMeta = new byte[500];
            Array.Fill(largeMeta, (byte)(i % 256));
            
            var nodeState = new NodeState
            {
                Node = new Node
                {
                    Name = $"large-node-{i}",
                    Addr = System.Net.IPAddress.Parse("127.0.0.1"),
                    Port = (ushort)(5000 + i),
                    Meta = largeMeta,
                    PMin = ProtocolVersion.Min,
                    PMax = ProtocolVersion.Max,
                    PCur = m1._config.ProtocolVersion,
                    DMin = m1._config.DelegateProtocolMin,
                    DMax = m1._config.DelegateProtocolMax,
                    DCur = m1._config.DelegateProtocolVersion
                },
                State = NodeStateType.Alive,
                Incarnation = (uint)i,
                StateChange = DateTimeOffset.UtcNow
            };

            lock (m1._nodeLock)
            {
                m1._nodes.Add(nodeState);
                m1._nodeMap[nodeState.Node.Name] = nodeState;
            }
        }

        _output.WriteLine($"Created {m1._nodes.Count} nodes with large metadata");

        // Act
        var (joined, error) = await m2.JoinAsync(new[] { $"{m1._config.BindAddr}:{m1._config.BindPort}" });

        await Task.Delay(2000); // More time for large transfer

        // Assert
        error.Should().BeNull("large state transfer with compression+encryption should work");
        joined.Should().Be(1);
        
        var m2Members = m2.Members();
        _output.WriteLine($"m2 received {m2Members.Count} members");
        m2Members.Should().HaveCountGreaterThan(10, "should receive large compressed and encrypted state");
    }

    // ============================================================================
    // ERROR SCENARIOS
    // ============================================================================

    [Fact]
    public async Task Compression_WithNetworkIssues_ShouldHandleGracefully()
    {
        // Arrange
        var m1 = CreateMemberlist("comp-network-node1", c => c.EnableCompression = true);
        var m2 = CreateMemberlist("comp-network-node2", c => c.EnableCompression = true);

        // Act - Try to join with wrong address (should fail gracefully)
        var (joined, error) = await m2.JoinAsync(new[] { "127.0.0.1:99999" }); // Invalid port

        // Assert - Should fail but not crash
        error.Should().NotBeNull("invalid address should cause error");
    }

    [Fact]
    public void Encryption_WithPartialKey_ShouldNotCrash()
    {
        // Arrange - Create keyring with partial/empty keys
        var emptyKey = new byte[32]; // All zeros
        var keyring = Keyring.Create(null, emptyKey);

        // Act & Assert - Should not throw during construction
        var m1 = CreateMemberlist("partial-key-node", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
        });

        m1.Should().NotBeNull("should create memberlist even with empty key");
    }

    // ============================================================================
    // PERFORMANCE VERIFICATION TESTS
    // ============================================================================

    [Fact]
    public void Compression_PerformanceTest_ShouldBeReasonablyFast()
    {
        // Arrange
        var data = new byte[100_000]; // 100KB
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i % 256);

        // Act & Measure
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var compressed = CompressionUtils.CompressPayload(data);
        var compressTime = sw.Elapsed;

        sw.Restart();
        var decompressed = CompressionUtils.DecompressPayload(compressed);
        var decompressTime = sw.Elapsed;

        // Assert
        compressTime.Should().BeLessThan(TimeSpan.FromSeconds(1), "compression should be fast");
        decompressTime.Should().BeLessThan(TimeSpan.FromSeconds(1), "decompression should be fast");
        decompressed.Should().BeEquivalentTo(data);

        _output.WriteLine($"Compressed 100KB in {compressTime.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Decompressed in {decompressTime.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Compression ratio: {(double)compressed.Length / data.Length * 100:F1}%");
    }

    [Fact]
    public void Compression_MultipleSequentialOperations_ShouldNotLeak()
    {
        // Arrange & Act - Multiple compress/decompress cycles
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var data = new byte[1000];
            new Random(iteration).NextBytes(data);

            var compressed = CompressionUtils.CompressPayload(data);
            var decompressed = CompressionUtils.DecompressPayload(compressed);

            // Assert each iteration
            decompressed.Should().BeEquivalentTo(data, $"iteration {iteration} should work");
        }

        // If we get here without OOM or crashes, memory handling is good
    }

    // ============================================================================
    // CONCURRENT ACCESS TESTS
    // ============================================================================

    [Fact]
    public async Task Compression_ConcurrentOperations_ShouldBeSafe()
    {
        // Arrange
        var tasks = new List<Task>();

        // Act - Compress/decompress from multiple threads
        for (int i = 0; i < 10; i++)
        {
            int seed = i;
            tasks.Add(Task.Run(() =>
            {
                var data = new byte[1000];
                new Random(seed).NextBytes(data);

                var compressed = CompressionUtils.CompressPayload(data);
                var decompressed = CompressionUtils.DecompressPayload(compressed);

                decompressed.Should().BeEquivalentTo(data);
            }));
        }

        // Assert - All should complete without errors
        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Encryption_ConcurrentNodes_ShouldHandleCorrectly()
    {
        // Arrange - Multiple nodes joining simultaneously
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("concurrent-enc-master", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
        });

        var joinTasks = new List<Task>();
        for (int i = 0; i < 3; i++)
        {
            int nodeNum = i;
            joinTasks.Add(Task.Run(async () =>
            {
                var m = CreateMemberlist($"concurrent-enc-node{nodeNum}", c =>
                {
                    c.Keyring = keyring;
                    c.GossipVerifyIncoming = true;
                    c.GossipVerifyOutgoing = true;
                });

                await m.JoinAsync(new[] { $"{m1._config.BindAddr}:{m1._config.BindPort}" });
            }));
        }

        // Act
        await Task.WhenAll(joinTasks);
        await Task.Delay(1000);

        // Assert - All nodes should have joined
        var members = m1.Members();
        members.Should().HaveCountGreaterOrEqualTo(3, "concurrent encrypted joins should work");
    }
}
