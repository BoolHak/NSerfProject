using FluentAssertions;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.Transport;
using NSerf.Memberlist.Messages;
using System.Collections.Concurrent;
using Xunit;

namespace NSerfTests.Memberlist;

/// <summary>
/// Tests for user message handling functionality.
/// </summary>
public class UserMessageTests : IDisposable
{
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = new();
    
    public void Dispose()
    {
        foreach (var m in _memberlists)
        {
            m.ShutdownAsync().GetAwaiter().GetResult();
        }
    }
    
    private MemberlistConfig CreateTestConfig(string name, TestDelegate? testDelegate = null)
    {
        var config = new MemberlistConfig
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
            DeadNodeReclaimTime = TimeSpan.FromSeconds(30),
            Delegate = testDelegate
        };
        
        return config;
    }
    
    private NSerf.Memberlist.Memberlist CreateMemberlistAsync(MemberlistConfig config)
    {
        // Create real network transport
        var transportConfig = new NetTransportConfig
        {
            BindAddrs = new List<string> { config.BindAddr },
            BindPort = config.BindPort,
            Logger = null
        };
        
        var transport = NetTransport.Create(transportConfig);
        config.Transport = transport;
        
        var m = NSerf.Memberlist.Memberlist.Create(config);
        _memberlists.Add(m);
        return m;
    }
    
    [Fact]
    public async Task UserMessage_UDP_DeliveredToDelegate()
    {
        // Arrange - Create delegate to capture messages
        var receivedMessages = new ConcurrentBag<byte[]>();
        var testDelegate = new TestDelegate(receivedMessages);
        
        // Create two nodes
        var config1 = CreateTestConfig("node1");
        var m1 =  CreateMemberlistAsync(config1);
        
        var config2 = CreateTestConfig("node2", testDelegate);
        var m2 =  CreateMemberlistAsync(config2);
        
        // Join nodes
        var bindPort = m1.Config.BindPort;
        var joinAddr = $"{m1.Config.BindAddr}:{bindPort}";
        using var joinCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync(new[] { joinAddr }, joinCts.Token);
        
        error.Should().BeNull();
        numJoined.Should().Be(1);
        
        await Task.Delay(200);
        
        // Act - Send a user message via UDP
        var testMessage = System.Text.Encoding.UTF8.GetBytes("Hello from node1!");
        var userMessageBytes = new byte[1 + testMessage.Length];
        userMessageBytes[0] = (byte)MessageType.User;
        Array.Copy(testMessage, 0, userMessageBytes, 1, testMessage.Length);
        
        var m2Addr = new Address
        {
            Addr = $"{m2.Config.BindAddr}:{m2.Config.BindPort}",
            Name = m2.Config.Name
        };
        
        var transport = (m1.Config.Transport as INodeAwareTransport)!;
        await transport.WriteToAddressAsync(userMessageBytes, m2Addr, CancellationToken.None);
        
        // Assert - Wait for message to be received
        await Task.Delay(500);
        
        receivedMessages.Should().NotBeEmpty("delegate should receive user message");
        receivedMessages.Should().HaveCountGreaterThanOrEqualTo(1);
        
        var receivedMsg = receivedMessages.FirstOrDefault();
        receivedMsg.Should().NotBeNull();
        
        var receivedText = System.Text.Encoding.UTF8.GetString(receivedMsg!);
        receivedText.Should().Be("Hello from node1!");
    }
    
    /// <summary>
    /// Simple test delegate that captures user messages.
    /// </summary>
    private class TestDelegate : IDelegate
    {
        private readonly ConcurrentBag<byte[]> _messages;
        
        public TestDelegate(ConcurrentBag<byte[]> messages)
        {
            _messages = messages;
        }
        
        public byte[] NodeMeta(int limit) => Array.Empty<byte>();
        
        public void NotifyMsg(ReadOnlySpan<byte> message)
        {
            // Copy the message since it may be modified after return
            var copy = message.ToArray();
            _messages.Add(copy);
        }
        
        public List<byte[]> GetBroadcasts(int overhead, int limit) => new();
        
        public byte[] LocalState(bool join) => Array.Empty<byte>();
        
        public void MergeRemoteState(ReadOnlySpan<byte> buffer, bool join) { }
    }
}
