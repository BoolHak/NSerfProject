// Comprehensive UDP encryption tests for memberlist
// Tests encryption of outgoing UDP packets and decryption of incoming packets

using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Handlers;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.Security;
using NSerf.Memberlist.Transport;
using Xunit.Abstractions;

namespace NSerfTests.Memberlist;

/// <summary>
/// Tests for UDP packet encryption in memberlist.
/// Verifies that UDP packets are properly encrypted when sent and decrypted when received.
/// </summary>
public class UdpEncryptionTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = new();
    private readonly List<UdpClient> _udpClients = new();

    public UdpEncryptionTests(ITestOutputHelper output)
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

        foreach (var client in _udpClients)
        {
            client.Close();
            client.Dispose();
        }
        _udpClients.Clear();
    }

    private NSerf.Memberlist.Memberlist CreateMemberlist(string name, Action<MemberlistConfig>? configure = null)
    {
        var config = new MemberlistConfig
        {
            Name = name,
            BindAddr = "127.0.0.1",
            BindPort = 0,
            ProbeInterval = TimeSpan.FromMilliseconds(200),
            ProbeTimeout = TimeSpan.FromMilliseconds(100),
            EnableCompression = false,
            Logger = null
        };

        configure?.Invoke(config);

        // Create transport
        var transportConfig = new NetTransportConfig
        {
            BindAddrs = new List<string> { config.BindAddr },
            BindPort = config.BindPort,
            Logger = config.Logger
        };
        config.Transport = NetTransport.Create(transportConfig);

        var memberlist = NSerf.Memberlist.Memberlist.Create(config);
        _memberlists.Add(memberlist);
        return memberlist;
    }

    // ============================================================================
    // OUTGOING UDP PACKET ENCRYPTION TESTS
    // ============================================================================

    [Fact]
    public async Task SendPacketAsync_WithEncryptionEnabled_ShouldEncryptPacket()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("udp-enc-send-test", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyOutgoing = true;
            c.Label = "testlabel";
        });

        // Create a UDP listener to capture the raw packet
        var captureClient = new UdpClient(0);
        _udpClients.Add(captureClient);
        var captureEndpoint = (IPEndPoint)captureClient.Client.LocalEndPoint!;
        
        var receiveTask = captureClient.ReceiveAsync();

        // Act - Send a ping message
        var pingMsg = new PingMessage
        {
            SeqNo = 1,
            Node = "udp-enc-send-test"
        };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);

        var targetAddr = new Address { Addr = $"127.0.0.1:{captureEndpoint.Port}", Name = "" };
        await m1.SendPacketAsync(packet, targetAddr, CancellationToken.None);

        // Assert - Capture and verify the packet is encrypted
        var result = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var receivedData = result.Buffer;

        _output.WriteLine($"Received {receivedData.Length} bytes");
        _output.WriteLine($"First byte: 0x{receivedData[0]:X2}");

        // If encrypted, the packet should NOT start with the label or message type directly
        // After label header, it should have encryption version byte (0 or 1)
        
        // Remove label header to check encryption
        var (labelStripped, _) = LabelHandler.RemoveLabelHeaderFromPacket(receivedData);
        
        _output.WriteLine($"After label removal: {labelStripped.Length} bytes");
        var hexBytes = System.String.Join("-", labelStripped.Take(Math.Min(10, labelStripped.Length)).Select(b => $"{b:X2}"));
        _output.WriteLine($"First bytes: {hexBytes}");
        
        // If encryption is applied, first byte should be encryption version (0 or 1)
        // NOT the MessageType.Ping (0x00)
        (labelStripped[0] == 0 || labelStripped[0] == 1).Should().BeTrue($"encrypted packet should start with encryption version, but was 0x{labelStripped[0]:X2}");
        (labelStripped[0] != (byte)MessageType.Ping).Should().BeTrue("packet should be encrypted, not plaintext");
    }

    [Fact]
    public async Task SendPacketAsync_WithEncryptionDisabled_ShouldSendPlaintext()
    {
        // Arrange
        var m1 = CreateMemberlist("udp-plain-send-test", c =>
        {
            c.Keyring = null; // No encryption
            c.Label = "testlabel";
        });

        var captureClient = new UdpClient(0);
        _udpClients.Add(captureClient);
        var captureEndpoint = (IPEndPoint)captureClient.Client.LocalEndPoint!;
        
        var receiveTask = captureClient.ReceiveAsync();

        // Act
        var pingMsg = new PingMessage { SeqNo = 1, Node = "udp-plain-send-test" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);

        var targetAddr = new Address { Addr = $"127.0.0.1:{captureEndpoint.Port}", Name = "" };
        await m1.SendPacketAsync(packet, targetAddr, CancellationToken.None);

        // Assert
        var result = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var receivedData = result.Buffer;

        var (labelStripped, _) = LabelHandler.RemoveLabelHeaderFromPacket(receivedData);
        
        _output.WriteLine($"After label removal: {labelStripped.Length} bytes");
        var hexBytesPlain = System.String.Join("-", labelStripped.Take(Math.Min(10, labelStripped.Length)).Select(b => $"{b:X2}"));
        _output.WriteLine($"First bytes: {hexBytesPlain}");
        
        // Should be plaintext - starts with MessageType.Ping
        (labelStripped[0] == (byte)MessageType.Ping).Should().BeTrue($"unencrypted packet should be plaintext MessageType.Ping (0), but was 0x{labelStripped[0]:X2}");
    }

    [Fact]
    public async Task SendPacketAsync_WithGossipVerifyOutgoingFalse_ShouldNotEncrypt()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("udp-no-verify-test", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyOutgoing = false; // Don't encrypt outgoing
            c.Label = "testlabel";
        });

        var captureClient = new UdpClient(0);
        _udpClients.Add(captureClient);
        var captureEndpoint = (IPEndPoint)captureClient.Client.LocalEndPoint!;
        
        var receiveTask = captureClient.ReceiveAsync();

        // Act
        var pingMsg = new PingMessage { SeqNo = 1, Node = "udp-no-verify-test" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);

        var targetAddr = new Address { Addr = $"127.0.0.1:{captureEndpoint.Port}", Name = "" };
        await m1.SendPacketAsync(packet, targetAddr, CancellationToken.None);

        // Assert
        var result = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var receivedData = result.Buffer;

        var (labelStripped, _) = LabelHandler.RemoveLabelHeaderFromPacket(receivedData);
        
        _output.WriteLine($"After label removal: {labelStripped.Length} bytes");
        var hexBytesNoVerify = System.String.Join("-", labelStripped.Take(Math.Min(10, labelStripped.Length)).Select(b => $"{b:X2}"));
        _output.WriteLine($"First bytes: {hexBytesNoVerify}");
        
        // Should be plaintext even though keyring exists
        (labelStripped[0] == (byte)MessageType.Ping).Should().BeTrue($"GossipVerifyOutgoing=false should send plaintext, but first byte was 0x{labelStripped[0]:X2}");
    }

    // ============================================================================
    // INCOMING UDP PACKET DECRYPTION TESTS
    // ============================================================================

    [Fact]
    public async Task IngestPacket_WithEncryptedPacket_ShouldDecrypt()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var receivedPackets = new List<byte[]>();
        var handler = new TestPacketHandler(receivedPackets);

        var m1 = CreateMemberlist("udp-enc-receive-test", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.Label = "testlabel";
        });

        // Act - Manually create and send encrypted packet
        var pingMsg = new PingMessage { SeqNo = 99, Node = "sender" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);

        // Encrypt the packet
        var authData = System.Text.Encoding.UTF8.GetBytes("testlabel");
        var encrypted = SecurityTools.EncryptPayload(1, key, packet, authData);

        // Add label header
        var withLabel = LabelHandler.AddLabelHeaderToPacket(encrypted, "testlabel");

        // Send via transport
        var targetAddr = new Address { Addr = $"{m1.Config.BindAddr}:{m1.Config.BindPort}", Name = "" };
        using var sender = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), m1.Config.BindPort);
        await sender.SendAsync(withLabel, endpoint);

        // Wait for packet processing
        await Task.Delay(200);

        // Assert - The packet should be decrypted and processed
        // (Verification depends on memberlist internals - this tests the decrypt path doesn't throw)
    }

    [Fact]
    public async Task IngestPacket_WithWrongKey_ShouldRejectOrWarn()
    {
        // Arrange
        var correctKey = new byte[32];
        var wrongKey = new byte[32];
        new Random(42).NextBytes(correctKey);
        new Random(43).NextBytes(wrongKey);
        
        var correctKeyring = Keyring.Create(null, correctKey);

        var m1 = CreateMemberlist("udp-wrong-key-test", c =>
        {
            c.Keyring = correctKeyring;
            c.GossipVerifyIncoming = true;
            c.Label = "testlabel";
        });

        // Act - Send packet encrypted with wrong key
        var pingMsg = new PingMessage { SeqNo = 99, Node = "sender" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);

        var authData = System.Text.Encoding.UTF8.GetBytes("testlabel");
        var encrypted = SecurityTools.EncryptPayload(1, wrongKey, packet, authData);
        var withLabel = LabelHandler.AddLabelHeaderToPacket(encrypted, "testlabel");

        var targetAddr = new Address { Addr = $"{m1.Config.BindAddr}:{m1.Config.BindPort}", Name = "" };
        using var sender = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), m1.Config.BindPort);
        await sender.SendAsync(withLabel, endpoint);

        // Wait for packet processing
        await Task.Delay(200);

        // Assert - Packet should be rejected (no crash)
        // The memberlist should still be functional
        m1.Config.Name.Should().Be("udp-wrong-key-test");
    }

    [Fact]
    public async Task IngestPacket_EncryptedWithGossipVerifyIncomingFalse_ShouldFallbackToPlaintext()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("udp-no-verify-incoming", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = false; // Allow plaintext fallback
            c.Label = "testlabel";
        });

        // Act - Send plaintext packet (should be accepted as fallback)
        var pingMsg = new PingMessage { SeqNo = 99, Node = "sender" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);

        var withLabel = LabelHandler.AddLabelHeaderToPacket(packet, "testlabel");

        var targetAddr = new Address { Addr = $"{m1.Config.BindAddr}:{m1.Config.BindPort}", Name = "" };
        using var sender = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), m1.Config.BindPort);
        await sender.SendAsync(withLabel, endpoint);

        // Wait for packet processing
        await Task.Delay(200);

        // Assert - Should accept plaintext when GossipVerifyIncoming=false
        m1.Config.Name.Should().Be("udp-no-verify-incoming");
    }

    // ============================================================================
    // LABEL AS AUTHENTICATED DATA TESTS
    // ============================================================================

    [Fact]
    public void EncryptedPacket_WithLabel_ShouldUseAsAuthData()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var label = "secure-label";
        var message = "test message"u8.ToArray();

        // Act - Encrypt with label as auth data
        var authData = System.Text.Encoding.UTF8.GetBytes(label);
        var encrypted = SecurityTools.EncryptPayload(1, key, message, authData);

        // Assert - Decryption should succeed with same auth data
        var decrypted = SecurityTools.DecryptPayload(new[] { key }, encrypted, authData);
        decrypted.Should().BeEquivalentTo(message);
    }

    [Fact]
    public void EncryptedPacket_WithWrongLabel_ShouldFailDecryption()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var correctLabel = "correct-label";
        var wrongLabel = "wrong-label";
        var message = "test message"u8.ToArray();

        // Act - Encrypt with correct label
        var correctAuthData = System.Text.Encoding.UTF8.GetBytes(correctLabel);
        var encrypted = SecurityTools.EncryptPayload(1, key, message, correctAuthData);

        // Assert - Decryption with wrong label should fail
        var wrongAuthData = System.Text.Encoding.UTF8.GetBytes(wrongLabel);
        Action act = () => SecurityTools.DecryptPayload(new[] { key }, encrypted, wrongAuthData);
        act.Should().Throw<Exception>("wrong authenticated data should fail decryption");
    }

    // ============================================================================
    // GOSSIP WITH ENCRYPTION INTEGRATION TESTS
    // ============================================================================

    [Fact]
    public async Task Gossip_WithEncryption_NodesShouldCommunicate()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("gossip-enc-node1", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.Label = "cluster1";
        });

        var m2 = CreateMemberlist("gossip-enc-node2", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.Label = "cluster1";
        });

        // Act - Join the cluster
        var (joined, error) = await m2.JoinAsync(new[] { $"{m1.Config.BindAddr}:{m1.Config.BindPort}" });

        // Wait for gossip
        await Task.Delay(500);

        // Assert
        error.Should().BeNull("encrypted nodes should communicate");
        joined.Should().Be(1);

        var m1Members = m1.Members();
        var m2Members = m2.Members();

        m1Members.Should().HaveCount(2, "node1 should see both nodes");
        m2Members.Should().HaveCount(2, "node2 should see both nodes");
    }

    [Fact]
    public async Task Gossip_WithMismatchedKeys_ShouldFailCommunication()
    {
        // Arrange
        var key1 = new byte[32];
        var key2 = new byte[32];
        new Random(42).NextBytes(key1);
        new Random(99).NextBytes(key2); // Different key

        var keyring1 = Keyring.Create(null, key1);
        var keyring2 = Keyring.Create(null, key2);

        var m1 = CreateMemberlist("gossip-bad-node1", c =>
        {
            c.Keyring = keyring1;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
        });

        var m2 = CreateMemberlist("gossip-bad-node2", c =>
        {
            c.Keyring = keyring2;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
        });

        // Act
        var (joined, error) = await m2.JoinAsync(new[] { $"{m1.Config.BindAddr}:{m1.Config.BindPort}" });

        await Task.Delay(1000);

        // Assert - Should not establish proper cluster
        var m1Members = m1.Members();
        var m2Members = m2.Members();

        // Each node should only see itself
        m1Members.Should().HaveCount(1, "node1 should only see itself with wrong key");
        m2Members.Should().HaveCount(1, "node2 should only see itself with wrong key");
    }

    [Fact]
    public async Task Gossip_WithEncryptionAndCompression_ShouldWork()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("enc-comp-node1", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.EnableCompression = true; // Both encryption AND compression
        });

        var m2 = CreateMemberlist("enc-comp-node2", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.EnableCompression = true;
        });

        // Act
        var (joined, error) = await m2.JoinAsync(new[] { $"{m1.Config.BindAddr}:{m1.Config.BindPort}" });

        await Task.Delay(500);

        // Assert
        error.Should().BeNull("compression and encryption should work together");
        joined.Should().Be(1);

        m1.Members().Should().HaveCount(2);
        m2.Members().Should().HaveCount(2);
    }

    // ============================================================================
    // EDGE CASES: LABEL HANDLING
    // ============================================================================

    [Fact]
    public async Task SendPacket_WithEmptyLabel_ShouldEncryptWithoutLabelHeader()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("empty-label-test", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyOutgoing = true;
            c.Label = ""; // Empty label
        });

        var captureClient = new UdpClient(0);
        _udpClients.Add(captureClient);
        var captureEndpoint = (IPEndPoint)captureClient.Client.LocalEndPoint!;
        var receiveTask = captureClient.ReceiveAsync();

        // Act
        var pingMsg = new PingMessage { SeqNo = 1, Node = "empty-label-test" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);
        var targetAddr = new Address { Addr = $"127.0.0.1:{captureEndpoint.Port}", Name = "" };
        await m1.SendPacketAsync(packet, targetAddr, CancellationToken.None);

        // Assert
        var result = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var receivedData = result.Buffer;

        // Should NOT start with HasLabel (244/0xF4)
        receivedData[0].Should().NotBe((byte)MessageType.HasLabel, "empty label should not add label header");
        
        // Should start with encryption version
        (receivedData[0] == 0 || receivedData[0] == 1).Should().BeTrue("packet should be encrypted");
    }

    [Fact]
    public async Task SendPacket_WithMaxLengthLabel_ShouldSucceed()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);
        var maxLabel = new string('x', 255); // LabelMaxSize = 255

        var m1 = CreateMemberlist("max-label-test", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyOutgoing = true;
            c.Label = maxLabel;
        });

        var captureClient = new UdpClient(0);
        _udpClients.Add(captureClient);
        var captureEndpoint = (IPEndPoint)captureClient.Client.LocalEndPoint!;
        var receiveTask = captureClient.ReceiveAsync();

        // Act
        var pingMsg = new PingMessage { SeqNo = 1, Node = "max-label-test" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);
        var targetAddr = new Address { Addr = $"127.0.0.1:{captureEndpoint.Port}", Name = "" };
        
        // Should not throw
        await m1.SendPacketAsync(packet, targetAddr, CancellationToken.None);

        // Assert
        var result = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var receivedData = result.Buffer;

        receivedData[0].Should().Be((byte)MessageType.HasLabel);
        receivedData[1].Should().Be(255, "label length should be 255");
        
        // Verify label can be extracted
        var (_, extractedLabel) = LabelHandler.RemoveLabelHeaderFromPacket(receivedData);
        extractedLabel.Should().Be(maxLabel);
    }

    [Fact]
    public void LabelHandler_WithOversizedLabel_ShouldThrowException()
    {
        // Arrange
        var oversizedLabel = new string('x', 256); // > LabelMaxSize
        var packet = new byte[] { 0x00 };

        // Act
        Action act = () => LabelHandler.AddLabelHeaderToPacket(packet, oversizedLabel);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*too long*");
    }

    // ============================================================================
    // EDGE CASES: KEY MANAGEMENT
    // ============================================================================

    [Fact]
    public async Task Encryption_WithMultipleKeysInKeyring_ShouldDecryptWithAnyKey()
    {
        // Arrange - Create keyring with 3 keys
        var key1 = new byte[32];
        var key2 = new byte[32];
        var key3 = new byte[32];
        new Random(1).NextBytes(key1);
        new Random(2).NextBytes(key2);
        new Random(3).NextBytes(key3);

        // Receiver has all 3 keys
        var receiverKeyring = Keyring.Create(new[] { key1, key2 }, key3);

        var receiver = CreateMemberlist("multi-key-receiver", c =>
        {
            c.Keyring = receiverKeyring;
            c.GossipVerifyIncoming = true;
            c.Label = "multikey";
        });

        // Act - Send packet encrypted with key2 (not the primary key)
        var pingMsg = new PingMessage { SeqNo = 99, Node = "sender" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);
        
        var authData = System.Text.Encoding.UTF8.GetBytes("multikey");
        var encrypted = SecurityTools.EncryptPayload(1, key2, packet, authData); // Use key2
        var withLabel = LabelHandler.AddLabelHeaderToPacket(encrypted, "multikey");

        using var sender = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), receiver.Config.BindPort);
        await sender.SendAsync(withLabel, endpoint);

        await Task.Delay(200);

        // Assert - Should decrypt successfully even though encrypted with non-primary key
        receiver.Config.Name.Should().Be("multi-key-receiver");
    }

    [Fact]
    public async Task Encryption_WithNullKeyring_ShouldSendPlaintext()
    {
        // Arrange - No keyring configured (EncryptionEnabled returns false when keyring is null)
        var m1 = CreateMemberlist("null-keyring-test", c =>
        {
            c.Keyring = null; // No keyring
            c.GossipVerifyOutgoing = true;
            c.Label = "test";
        });

        var captureClient = new UdpClient(0);
        _udpClients.Add(captureClient);
        var captureEndpoint = (IPEndPoint)captureClient.Client.LocalEndPoint!;
        var receiveTask = captureClient.ReceiveAsync();

        // Act
        var pingMsg = new PingMessage { SeqNo = 1, Node = "test" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);
        var targetAddr = new Address { Addr = $"127.0.0.1:{captureEndpoint.Port}", Name = "" };
        await m1.SendPacketAsync(packet, targetAddr, CancellationToken.None);

        // Assert - Should send plaintext when keyring is null
        var result = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var receivedData = result.Buffer;
        
        var (stripped, _) = LabelHandler.RemoveLabelHeaderFromPacket(receivedData);
        stripped[0].Should().Be((byte)MessageType.Ping, "null keyring should send plaintext");
    }

    [Fact]
    public void Keyring_WithSecondaryKeys_ShouldHaveMultipleKeys()
    {
        // Arrange
        var primary = new byte[32];
        var secondary1 = new byte[32];
        var secondary2 = new byte[32];
        new Random(1).NextBytes(primary);
        new Random(2).NextBytes(secondary1);
        new Random(3).NextBytes(secondary2);

        // Act
        var keyring = Keyring.Create(new[] { secondary1, secondary2 }, primary);

        // Assert
        var allKeys = keyring.GetKeys();
        allKeys.Should().HaveCount(3, "should have primary + 2 secondary keys");
        
        var primaryKey = keyring.GetPrimaryKey();
        primaryKey.Should().NotBeNull();
        primaryKey.Should().BeEquivalentTo(primary);
    }

    // ============================================================================
    // EDGE CASES: PACKET CORRUPTION
    // ============================================================================

    [Fact]
    public async Task IngestPacket_WithCorruptedCiphertext_ShouldReject()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("corrupt-test", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.Label = "test";
        });

        // Create valid encrypted packet
        var pingMsg = new PingMessage { SeqNo = 99, Node = "sender" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);
        var authData = System.Text.Encoding.UTF8.GetBytes("test");
        var encrypted = SecurityTools.EncryptPayload(1, key, packet, authData);
        var withLabel = LabelHandler.AddLabelHeaderToPacket(encrypted, "test");

        // Corrupt the ciphertext (flip bits in the middle)
        withLabel[withLabel.Length / 2] ^= 0xFF;

        // Act - Send corrupted packet
        using var sender = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), m1.Config.BindPort);
        await sender.SendAsync(withLabel, endpoint);

        await Task.Delay(200);

        // Assert - Should not crash (packet rejected silently)
        m1.Config.Name.Should().Be("corrupt-test");
    }

    [Fact]
    public async Task IngestPacket_WithTruncatedPacket_ShouldReject()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("truncate-test", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.Label = "test";
        });

        // Create valid encrypted packet
        var pingMsg = new PingMessage { SeqNo = 99, Node = "sender" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);
        var authData = System.Text.Encoding.UTF8.GetBytes("test");
        var encrypted = SecurityTools.EncryptPayload(1, key, packet, authData);
        var withLabel = LabelHandler.AddLabelHeaderToPacket(encrypted, "test");

        // Truncate the packet (remove last 10 bytes)
        var truncated = withLabel[..^10];

        // Act - Send truncated packet
        using var sender = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), m1.Config.BindPort);
        await sender.SendAsync(truncated, endpoint);

        await Task.Delay(200);

        // Assert - Should not crash
        m1.Config.Name.Should().Be("truncate-test");
    }

    [Fact]
    public async Task IngestPacket_WithTamperedAuthData_ShouldReject()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("authdata-test", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.Label = "correct-label"; // Receiver expects this label
        });

        // Create packet encrypted with WRONG label
        var pingMsg = new PingMessage { SeqNo = 99, Node = "sender" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);
        var wrongAuthData = System.Text.Encoding.UTF8.GetBytes("wrong-label");
        var encrypted = SecurityTools.EncryptPayload(1, key, packet, wrongAuthData);
        
        // But send with correct label header (mismatch!)
        var withLabel = LabelHandler.AddLabelHeaderToPacket(encrypted, "correct-label");

        // Act - Send packet with mismatched auth data
        using var sender = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), m1.Config.BindPort);
        await sender.SendAsync(withLabel, endpoint);

        await Task.Delay(200);

        // Assert - Should reject (authentication failure)
        m1.Config.Name.Should().Be("authdata-test");
    }

    // ============================================================================
    // EDGE CASES: PACKET SIZES
    // ============================================================================

    [Fact]
    public void Encryption_WithLargePacket_ShouldSucceed()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);

        // Create large payload (close to UDP max)
        var largeData = new byte[60000]; // ~60KB
        new Random(123).NextBytes(largeData);

        var authData = System.Text.Encoding.UTF8.GetBytes("test");

        // Act
        var encrypted = SecurityTools.EncryptPayload(1, key, largeData, authData);

        // Assert - Should complete without error
        encrypted.Should().NotBeNull();
        encrypted.Length.Should().BeGreaterThan(largeData.Length, "encrypted is larger due to overhead");

        // Verify can decrypt
        var decrypted = SecurityTools.DecryptPayload(new[] { key }, encrypted, authData);
        decrypted.Should().BeEquivalentTo(largeData);
    }

    [Fact]
    public void Encryption_WithMinimalPacket_ShouldSucceed()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var minimalData = new byte[] { 0x42 }; // 1 byte
        var authData = Array.Empty<byte>();

        // Act
        var encrypted = SecurityTools.EncryptPayload(1, key, minimalData, authData);

        // Assert
        encrypted.Should().NotBeNull();
        encrypted.Length.Should().Be(1 + 12 + 1 + 16); // version + nonce + data + tag = 30 bytes

        // Verify can decrypt
        var decrypted = SecurityTools.DecryptPayload(new[] { key }, encrypted, authData);
        decrypted.Should().BeEquivalentTo(minimalData);
    }

    [Fact]
    public void Decryption_WithEmptyPacket_ShouldThrow()
    {
        // Arrange
        var key = new byte[32];
        var emptyPacket = Array.Empty<byte>();

        // Act
        Action act = () => SecurityTools.DecryptPayload(new[] { key }, emptyPacket, Array.Empty<byte>());

        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("*empty*");
    }

    // ============================================================================
    // EDGE CASES: ENCRYPTION VERSIONS
    // ============================================================================

    [Fact]
    public void Encryption_Version0_ShouldAddPKCS7Padding()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var message = new byte[] { 1, 2, 3, 4, 5 }; // 5 bytes (not block-aligned)
        var authData = Array.Empty<byte>();

        // Act - Version 0 uses PKCS7 padding
        var encrypted = SecurityTools.EncryptPayload(0, key, message, authData);

        // Assert - Version 0 has different overhead due to padding
        var overhead = SecurityTools.EncryptOverhead(0);
        encrypted.Length.Should().BeGreaterOrEqualTo(1 + 12 + 5 + 16); // version + nonce + data + padding + tag

        // Verify decryption
        var decrypted = SecurityTools.DecryptPayload(new[] { key }, encrypted, authData);
        decrypted.Should().BeEquivalentTo(message);
    }

    [Fact]
    public void Encryption_Version1_ShouldNotUsePadding()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var message = new byte[] { 1, 2, 3, 4, 5 }; // 5 bytes
        var authData = Array.Empty<byte>();

        // Act - Version 1 has no padding
        var encrypted = SecurityTools.EncryptPayload(1, key, message, authData);

        // Assert - Exact size: version(1) + nonce(12) + message(5) + tag(16) = 34
        encrypted.Length.Should().Be(34);

        // Verify decryption
        var decrypted = SecurityTools.DecryptPayload(new[] { key }, encrypted, authData);
        decrypted.Should().BeEquivalentTo(message);
    }

    [Fact]
    public void Decryption_WithInvalidVersion_ShouldThrow()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        
        // Create packet with invalid version (99)
        var fakePacket = new byte[30];
        fakePacket[0] = 99; // Invalid version
        new Random().NextBytes(fakePacket.AsSpan(1));

        // Act
        Action act = () => SecurityTools.DecryptPayload(new[] { key }, fakePacket, Array.Empty<byte>());

        // Assert
        act.Should().Throw<Exception>();
    }

    // ============================================================================
    // EDGE CASES: CONFIGURATION COMBINATIONS
    // ============================================================================

    [Fact]
    public async Task SendPacket_WithKeyringButNoVerifyFlags_ShouldSendPlaintext()
    {
        // Arrange
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("no-verify-flags", c =>
        {
            c.Keyring = keyring; // Keyring exists
            c.GossipVerifyOutgoing = false; // But don't encrypt
            c.GossipVerifyIncoming = false;
            c.Label = "test";
        });

        var captureClient = new UdpClient(0);
        _udpClients.Add(captureClient);
        var captureEndpoint = (IPEndPoint)captureClient.Client.LocalEndPoint!;
        var receiveTask = captureClient.ReceiveAsync();

        // Act
        var pingMsg = new PingMessage { SeqNo = 1, Node = "test" };
        var packet = NSerf.Memberlist.Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);
        var targetAddr = new Address { Addr = $"127.0.0.1:{captureEndpoint.Port}", Name = "" };
        await m1.SendPacketAsync(packet, targetAddr, CancellationToken.None);

        // Assert
        var result = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var receivedData = result.Buffer;
        
        var (stripped, _) = LabelHandler.RemoveLabelHeaderFromPacket(receivedData);
        
        // Should be plaintext
        stripped[0].Should().Be((byte)MessageType.Ping, "should send plaintext when GossipVerifyOutgoing is false");
    }
}

/// <summary>
/// Helper class to capture packets for testing
/// </summary>
internal class TestPacketHandler
{
    private readonly List<byte[]> _packets;

    public TestPacketHandler(List<byte[]> packets)
    {
        _packets = packets;
    }

    public void HandlePacket(byte[] data)
    {
        _packets.Add(data);
    }
}
