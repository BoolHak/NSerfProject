using FluentAssertions;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.Transport;
using Xunit;

namespace NSerfTests.Memberlist;

/// <summary>
/// Integration tests for packet label validation in PacketHandler.
/// </summary>
public class PacketLabelIntegrationTests : IDisposable
{
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = [];

    public void Dispose()
    {
        foreach (var m in _memberlists)
        {
            m.ShutdownAsync().GetAwaiter().GetResult();
        }
    }

    private MemberlistConfig CreateTestConfig(string name, string label = "")
    {
        return new MemberlistConfig
        {
            Name = name,
            BindAddr = "127.0.0.1",
            BindPort = 0,
            Label = label,
            Logger = null,
            ProbeInterval = TimeSpan.FromMilliseconds(100),
            ProbeTimeout = TimeSpan.FromMilliseconds(500),
            GossipInterval = TimeSpan.FromMilliseconds(100),
            TCPTimeout = TimeSpan.FromSeconds(2)
        };
    }

    private NSerf.Memberlist.Memberlist CreateMemberlistAsync(MemberlistConfig config)
    {
        var transportConfig = new NetTransportConfig
        {
            BindAddrs = [config.BindAddr],
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
    public async Task PacketLabels_MatchingLabels_ShouldCommunicate()
    {
        // Arrange - Create two nodes with same label
        var config1 = CreateTestConfig("node1", "production");
        var m1 = CreateMemberlistAsync(config1);

        var config2 = CreateTestConfig("node2", "production");
        var m2 = CreateMemberlistAsync(config2);

        // Act - Join nodes
        var joinAddr = $"{m1._config.BindAddr}:{m1._config.BindPort}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync([joinAddr], cts.Token);

        // Assert - Should successfully join
        error.Should().BeNull();
        numJoined.Should().Be(1);

        await Task.Delay(300);
        m1.NumMembers().Should().Be(2);
        m2.NumMembers().Should().Be(2);
    }

    [Fact]
    public async Task PacketLabels_MismatchedLabels_ShouldNotCommunicate()
    {
        // Label headers are now added to outgoing packets
        // This test verifies that nodes with different labels cannot communicate

        // Arrange - Create two nodes with different labels
        var config1 = CreateTestConfig("node1", "production");
        var m1 = CreateMemberlistAsync(config1);

        var config2 = CreateTestConfig("node2", "staging");
        var m2 = CreateMemberlistAsync(config2);

        // Act - Try to join nodes
        var joinAddr = $"{m1._config.BindAddr}:{m1._config.BindPort}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (numJoined, error) = await m2.JoinAsync([joinAddr], cts.Token);

        // Assert - Should fail to join due to label mismatch
        // The join might time out or return error
        await Task.Delay(500);

        // Nodes should not see each other
        m1.NumMembers().Should().Be(1, "node1 should only see itself");
        m2.NumMembers().Should().Be(1, "node2 should only see itself");
    }

    [Fact]
    public async Task PacketLabels_NoLabels_ShouldCommunicate()
    {
        // Arrange - Create two nodes with no labels
        var config1 = CreateTestConfig("node1", "");
        var m1 = CreateMemberlistAsync(config1);

        var config2 = CreateTestConfig("node2", "");
        var m2 = CreateMemberlistAsync(config2);

        // Act - Join nodes
        var joinAddr = $"{m1._config.BindAddr}:{m1._config.BindPort}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync([joinAddr], cts.Token);

        // Assert - Should successfully join
        error.Should().BeNull();
        numJoined.Should().Be(1);

        await Task.Delay(300);
        m1.NumMembers().Should().Be(2);
        m2.NumMembers().Should().Be(2);
    }

    [Fact]
    public async Task PacketLabels_OneLabeledOneNot_ShouldNotCommunicate()
    {
        // Label headers are now added to outgoing packets
        // This test verifies that labeled and unlabeled nodes cannot communicate

        // Arrange - One node with label, one without
        var config1 = CreateTestConfig("node1", "production");
        var m1 = CreateMemberlistAsync(config1);

        var config2 = CreateTestConfig("node2", "");
        var m2 = CreateMemberlistAsync(config2);

        // Act - Try to join
        var joinAddr = $"{m1._config.BindAddr}:{m1._config.BindPort}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (numJoined, error) = await m2.JoinAsync([joinAddr], cts.Token);

        // Assert - Should not successfully communicate
        await Task.Delay(500);

        m1.NumMembers().Should().Be(1, "labeled node should only see itself");
        m2.NumMembers().Should().Be(1, "unlabeled node should only see itself");
    }

    [Fact]
    public void LabelHandler_RoundTrip_PreservesData()
    {
        // Arrange
        var originalData = new byte[] { (byte)MessageType.Ping, 1, 2, 3, 4 };
        var label = "test-cluster";

        // Act - Add label
        var labeled = LabelHandler.AddLabelHeaderToPacket(originalData, label);

        // Verify label was added
        labeled.Should().NotBeSameAs(originalData);
        labeled.Length.Should().BeGreaterThan(originalData.Length);

        // Act - Remove label
        var (unlabeled, extractedLabel) = LabelHandler.RemoveLabelHeaderFromPacket(labeled);

        // Assert
        extractedLabel.Should().Be(label);
        unlabeled.Should().BeEquivalentTo(originalData);
    }
}
