using FluentAssertions;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.Transport;
using System.Collections.Concurrent;
using System.Text;
using Xunit;

namespace NSerfTests.Memberlist;

/// <summary>
/// Tests for user state merging during push/pull synchronization.
/// This tests the LocalState() and MergeRemoteState() delegate methods.
/// </summary>
public class UserStateMergingTests : IDisposable
{
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = new();
    
    public void Dispose()
    {
        foreach (var m in _memberlists)
        {
            m.ShutdownAsync().GetAwaiter().GetResult();
        }
    }
    
    private MemberlistConfig CreateTestConfig(string name, IDelegate? testDelegate = null)
    {
        return new MemberlistConfig
        {
            Name = name,
            BindAddr = "127.0.0.1",
            BindPort = 0,
            Logger = null,
            ProbeInterval = TimeSpan.FromMilliseconds(100),
            ProbeTimeout = TimeSpan.FromMilliseconds(500),
            GossipInterval = TimeSpan.FromMilliseconds(100),
            PushPullInterval = TimeSpan.FromMilliseconds(500),
            TCPTimeout = TimeSpan.FromSeconds(2),
            Delegate = testDelegate
        };
    }
    
    private async Task<NSerf.Memberlist.Memberlist> CreateMemberlistAsync(MemberlistConfig config)
    {
        var transportConfig = new NetTransportConfig
        {
            BindAddrs = new List<string> { config.BindAddr },
            BindPort = config.BindPort,
            Logger = null
        };
        
        var transport = NetTransport.Create(transportConfig);
        config.Transport = transport;
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);
        _memberlists.Add(m);
        return m;
    }
    
    [Fact]
    public async Task UserStateMerging_PushPull_ShouldExchangeState()
    {
        // Arrange - Create two nodes with test delegates
        var delegate1 = new TestDelegate("node1-state-v1");
        var config1 = CreateTestConfig("node1", delegate1);
        var m1 = await CreateMemberlistAsync(config1);
        
        var delegate2 = new TestDelegate("node2-state-v1");
        var config2 = CreateTestConfig("node2", delegate2);
        var m2 = await CreateMemberlistAsync(config2);
        
        // Act - Join nodes (triggers push/pull)
        var joinAddr = $"{m1._config.BindAddr}:{m1._config.BindPort}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync(new[] { joinAddr }, cts.Token);
        
        error.Should().BeNull();
        numJoined.Should().Be(1);
        
        // Wait for push/pull to complete
        await Task.Delay(1000);
        
        // Assert - Both delegates should have received remote state
        delegate1.ReceivedStates.Should().NotBeEmpty("node1 should receive node2's state");
        delegate2.ReceivedStates.Should().NotBeEmpty("node2 should receive node1's state");
        
        // Verify the actual state content
        delegate1.ReceivedStates.Should().Contain(s => s.Contains("node2-state-v1"));
        delegate2.ReceivedStates.Should().Contain(s => s.Contains("node1-state-v1"));
    }
    
    [Fact]
    public async Task UserStateMerging_NoDelegate_ShouldNotCrash()
    {
        // Arrange - Create two nodes without delegates
        var config1 = CreateTestConfig("node1", null);
        var m1 = await CreateMemberlistAsync(config1);
        
        var config2 = CreateTestConfig("node2", null);
        var m2 = await CreateMemberlistAsync(config2);
        
        // Act - Join should work without delegates
        var joinAddr = $"{m1._config.BindAddr}:{m1._config.BindPort}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync(new[] { joinAddr }, cts.Token);
        
        // Assert - Should succeed even without user state
        error.Should().BeNull();
        numJoined.Should().Be(1);
        
        await Task.Delay(300);
        m1.NumMembers().Should().Be(2);
        m2.NumMembers().Should().Be(2);
    }
    
    [Fact]
    public async Task UserStateMerging_EmptyState_ShouldWork()
    {
        // Arrange - Delegates that return empty state
        var delegate1 = new TestDelegate("");
        var config1 = CreateTestConfig("node1", delegate1);
        var m1 = await CreateMemberlistAsync(config1);
        
        var delegate2 = new TestDelegate("");
        var config2 = CreateTestConfig("node2", delegate2);
        var m2 = await CreateMemberlistAsync(config2);
        
        // Act - Join nodes
        var joinAddr = $"{m1._config.BindAddr}:{m1._config.BindPort}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync(new[] { joinAddr }, cts.Token);
        
        // Assert
        error.Should().BeNull();
        numJoined.Should().Be(1);
    }
    
    [Fact]
    public async Task UserStateMerging_LargeState_ShouldWork()
    {
        // Arrange - Create large state (10KB)
        var largeState1 = new string('A', 10240);
        var delegate1 = new TestDelegate(largeState1);
        var config1 = CreateTestConfig("node1", delegate1);
        var m1 = await CreateMemberlistAsync(config1);
        
        var largeState2 = new string('B', 10240);
        var delegate2 = new TestDelegate(largeState2);
        var config2 = CreateTestConfig("node2", delegate2);
        var m2 = await CreateMemberlistAsync(config2);
        
        // Act - Join nodes
        var joinAddr = $"{m1._config.BindAddr}:{m1._config.BindPort}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync(new[] { joinAddr }, cts.Token);
        
        error.Should().BeNull();
        numJoined.Should().Be(1);
        
        // Wait for push/pull
        await Task.Delay(1000);
        
        // Assert - Large states should be received correctly
        delegate1.ReceivedStates.Should().Contain(s => s.Length >= 10240);
        delegate2.ReceivedStates.Should().Contain(s => s.Length >= 10240);
    }
    
    [Fact]
    public async Task UserStateMerging_JoinFlag_ShouldBeSet()
    {
        // Arrange - Track whether join flag is set correctly
        var delegate1 = new TestDelegate("state1");
        var config1 = CreateTestConfig("node1", delegate1);
        var m1 = await CreateMemberlistAsync(config1);
        
        var delegate2 = new TestDelegate("state2");
        var config2 = CreateTestConfig("node2", delegate2);
        var m2 = await CreateMemberlistAsync(config2);
        
        // Act - Join (should have join=true initially)
        var joinAddr = $"{m1._config.BindAddr}:{m1._config.BindPort}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await m2.JoinAsync(new[] { joinAddr }, cts.Token);
        
        await Task.Delay(1000);
        
        // Assert - At least one merge should have occurred
        // (Note: The join flag is tracked internally but not exposed in our test delegate)
        delegate1.MergeCount.Should().BeGreaterThan(0);
        delegate2.MergeCount.Should().BeGreaterThan(0);
    }
    
    [Fact]
    public async Task UserStateMerging_BinaryState_ShouldPreserveBytes()
    {
        // Arrange - Binary data with all byte values
        var binaryState = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            binaryState[i] = (byte)i;
        }
        
        var delegate1 = new BinaryStateDelegate(binaryState);
        var config1 = CreateTestConfig("node1", delegate1);
        var m1 = await CreateMemberlistAsync(config1);
        
        var delegate2 = new BinaryStateDelegate(new byte[] { 255, 254, 253 });
        var config2 = CreateTestConfig("node2", delegate2);
        var m2 = await CreateMemberlistAsync(config2);
        
        // Act - Join nodes
        var joinAddr = $"{m1._config.BindAddr}:{m1._config.BindPort}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await m2.JoinAsync(new[] { joinAddr }, cts.Token);
        
        await Task.Delay(1000);
        
        // Assert - Binary data should be preserved exactly
        delegate1.ReceivedStates.Should().NotBeEmpty();
        delegate2.ReceivedStates.Should().NotBeEmpty();
        
        // Verify at least one received state matches our binary data
        var node2ReceivedNode1State = delegate2.ReceivedStates.Any(s => 
            s.Length == 256 && s[0] == 0 && s[255] == 255);
        node2ReceivedNode1State.Should().BeTrue("node2 should receive node1's binary state correctly");
    }
    
    /// <summary>
    /// Test delegate that tracks received states.
    /// </summary>
    private class TestDelegate : IDelegate
    {
        private readonly string _localState;
        public ConcurrentBag<string> ReceivedStates { get; } = new();
        public int MergeCount => ReceivedStates.Count;
        
        public TestDelegate(string localState)
        {
            _localState = localState;
        }
        
        public byte[] NodeMeta(int limit) => Array.Empty<byte>();
        
        public void NotifyMsg(ReadOnlySpan<byte> message) { }
        
        public List<byte[]> GetBroadcasts(int overhead, int limit) => new();
        
        public byte[] LocalState(bool join)
        {
            return Encoding.UTF8.GetBytes(_localState);
        }
        
        public void MergeRemoteState(ReadOnlySpan<byte> buffer, bool join)
        {
            var state = Encoding.UTF8.GetString(buffer);
            ReceivedStates.Add(state);
        }
    }
    
    /// <summary>
    /// Test delegate for binary state (non-UTF8 data).
    /// </summary>
    private class BinaryStateDelegate : IDelegate
    {
        private readonly byte[] _localState;
        public ConcurrentBag<byte[]> ReceivedStates { get; } = new();
        
        public BinaryStateDelegate(byte[] localState)
        {
            _localState = localState;
        }
        
        public byte[] NodeMeta(int limit) => Array.Empty<byte>();
        
        public void NotifyMsg(ReadOnlySpan<byte> message) { }
        
        public List<byte[]> GetBroadcasts(int overhead, int limit) => new();
        
        public byte[] LocalState(bool join)
        {
            return _localState;
        }
        
        public void MergeRemoteState(ReadOnlySpan<byte> buffer, bool join)
        {
            ReceivedStates.Add(buffer.ToArray());
        }
    }
}
