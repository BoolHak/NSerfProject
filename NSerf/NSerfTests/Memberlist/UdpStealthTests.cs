using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Handlers;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.Security;
using NSerf.Memberlist.Transport;

namespace NSerfTests.Memberlist;

public class UdpStealthTests : IAsyncLifetime
{
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = new();

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
            ProbeInterval = TimeSpan.FromMilliseconds(200),
            ProbeTimeout = TimeSpan.FromMilliseconds(100),
            EnableCompression = false,
            Logger = null
        };

        configure?.Invoke(config);

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

    [Fact]
    public async Task IngestPacket_WithEncryptionEnabledAndVerifyIncomingFalse_AndStealthUdpEnabled_ShouldNotRespondToPlaintextPing()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("stealth-udp", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = false;
            c.GossipVerifyOutgoing = true;
            c.Label = "testlabel";
            c.StealthUdp = true;
        });

        using var sender = new UdpClient(0);
        var senderEp = (IPEndPoint)sender.Client.LocalEndPoint!;

        var pingMsg = new PingMessage { SeqNo = 123, Node = m1.Config.Name };
        var pingPacket = MessageEncoder.Encode(MessageType.Ping, pingMsg);
        var labeledPlaintext = LabelHandler.AddLabelHeaderToPacket(pingPacket, "testlabel");

        var target = new IPEndPoint(IPAddress.Parse("127.0.0.1"), m1.Config.BindPort);
        await sender.SendAsync(labeledPlaintext, target);

        var receiveTask = sender.ReceiveAsync();
        var completed = await Task.WhenAny(receiveTask, Task.Delay(300));

        completed.Should().NotBe(receiveTask, "stealth UDP should not respond to unauthenticated/unencrypted packets");

        if (receiveTask.IsCompleted)
        {
            var received = await receiveTask;
            throw new Exception($"Unexpected UDP response of {received.Buffer.Length} bytes from {received.RemoteEndPoint} to {senderEp}");
        }
    }

    [Fact]
    public async Task IngestPacket_WithEncryptionEnabledAndStealthUdpEnabled_ShouldRespondToValidEncryptedPing()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("stealth-udp-valid", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = true;
            c.GossipVerifyOutgoing = true;
            c.Label = "testlabel";
            c.StealthUdp = true;
        });

        using var sender = new UdpClient(0);

        var pingMsg = new PingMessage { SeqNo = 456, Node = m1.Config.Name };
        var pingPacket = MessageEncoder.Encode(MessageType.Ping, pingMsg);

        var authData = System.Text.Encoding.UTF8.GetBytes("testlabel");
        var encrypted = SecurityTools.EncryptPayload(1, key, pingPacket, authData);
        var labeledEncrypted = LabelHandler.AddLabelHeaderToPacket(encrypted, "testlabel");

        var target = new IPEndPoint(IPAddress.Parse("127.0.0.1"), m1.Config.BindPort);
        await sender.SendAsync(labeledEncrypted, target);

        var response = await sender.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
        response.RemoteEndPoint.Address.ToString().Should().Be("127.0.0.1");

        var (labelStripped, _) = LabelHandler.RemoveLabelHeaderFromPacket(response.Buffer);
        var decrypted = SecurityTools.DecryptPayload(new[] { key }, labelStripped, authData);

        decrypted.Length.Should().BeGreaterThan(1);
        decrypted[0].Should().Be((byte)MessageType.AckResp);

        var ack = MessageEncoder.Decode<AckRespMessage>(decrypted.AsSpan(1));
        ack.SeqNo.Should().Be(456);
    }

    [Fact]
    public async Task IngestPacket_WithEncryptionEnabledAndVerifyIncomingFalse_AndStealthUdpDisabled_ShouldRespondToPlaintextPing()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var keyring = Keyring.Create(null, key);

        var m1 = CreateMemberlist("non-stealth-udp", c =>
        {
            c.Keyring = keyring;
            c.GossipVerifyIncoming = false;
            c.GossipVerifyOutgoing = true;
            c.Label = "testlabel";
            c.StealthUdp = false;
        });

        using var sender = new UdpClient(0);

        var pingMsg = new PingMessage { SeqNo = 789, Node = m1.Config.Name };
        var pingPacket = MessageEncoder.Encode(MessageType.Ping, pingMsg);
        var labeledPlaintext = LabelHandler.AddLabelHeaderToPacket(pingPacket, "testlabel");

        var target = new IPEndPoint(IPAddress.Parse("127.0.0.1"), m1.Config.BindPort);
        await sender.SendAsync(labeledPlaintext, target);

        var response = await sender.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));

        var authData = System.Text.Encoding.UTF8.GetBytes("testlabel");
        var (labelStripped, _) = LabelHandler.RemoveLabelHeaderFromPacket(response.Buffer);
        var decrypted = SecurityTools.DecryptPayload(new[] { key }, labelStripped, authData);

        decrypted.Length.Should().BeGreaterThan(1);
        decrypted[0].Should().Be((byte)MessageType.AckResp);

        var ack = MessageEncoder.Decode<AckRespMessage>(decrypted.AsSpan(1));
        ack.SeqNo.Should().Be(789);
    }
}
