// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Reflection;
using FluentAssertions;
using Xunit;
using SerfNamespace = NSerf.Serf;
using NSerf.Serf;

namespace NSerfTests.Serf;

public class SerfCoordinateCleanupTest : IDisposable
{
    private readonly List<SerfNamespace.Serf> _serfs = new();

    public void Dispose()
    {
        foreach (var serf in _serfs)
        {
            try { serf.ShutdownAsync().GetAwaiter().GetResult(); } catch { }
        }
    }

    private Config TestConfig()
    {
        var baseCfg = TestHelpers.CreateTestConfig();
        return new Config
        {
            NodeName = baseCfg.NodeName,
            MemberlistConfig = baseCfg.MemberlistConfig,
            DisableCoordinates = false, // ensure coordinates are enabled
            ReapInterval = TimeSpan.FromMilliseconds(100),
            ReconnectInterval = TimeSpan.FromMilliseconds(500),
            ReconnectTimeout = TimeSpan.FromMilliseconds(200),
            TombstoneTimeout = TimeSpan.FromSeconds(3600)
        };
    }

    [Fact]
    public async Task Reap_ShouldCleanupCoordinates_ForExpiredMember()
    {
        // Arrange
        var serf = await SerfNamespace.Serf.CreateAsync(TestConfig());
        _serfs.Add(serf);

        // Pre-populate coordinate client with samples for node "nodeX"
        var other = new NSerf.Coordinate.Coordinate();
        serf.UpdateCoordinate("nodeX", other, TimeSpan.FromMilliseconds(42));

        // Pre-populate coordCache with an entry for nodeX via reflection
        var coordCacheField = typeof(SerfNamespace.Serf).GetField("_coordCache", BindingFlags.NonPublic | BindingFlags.Instance);
        coordCacheField.Should().NotBeNull("coord cache should exist for cleanup");
        var coordCache = coordCacheField!.GetValue(serf) as IDictionary<string, NSerf.Coordinate.Coordinate>;
        coordCache.Should().NotBeNull();
        coordCache!["nodeX"] = new NSerf.Coordinate.Coordinate();

        // Add expired failed member for nodeX so reaper erases it
        var expired = new NSerf.Serf.MemberInfo
        {
            Name = "nodeX",
            LeaveTime = DateTimeOffset.UtcNow.AddMilliseconds(-500),
            Member = new NSerf.Serf.Member
            {
                Name = "nodeX",
                Addr = IPAddress.Parse("127.0.0.1"),
                Port = 5001,
                Status = MemberStatus.Failed
            }
        };
        serf.FailedMembers.Add(expired);
        serf.MemberStates["nodeX"] = expired;

        // Act - wait for reaper to run
        await Task.Delay(400);

        // Assert - coord client forgot the node and coord cache entry removed
        // Check client internal samples via reflection
        var coordClientField = typeof(SerfNamespace.Serf).GetField("_coordClient", BindingFlags.NonPublic | BindingFlags.Instance);
        coordClientField.Should().NotBeNull();
        var client = coordClientField!.GetValue(serf);
        client.Should().NotBeNull();

        var latencySamplesField = client!.GetType().GetField("_latencyFilterSamples", BindingFlags.NonPublic | BindingFlags.Instance);
        latencySamplesField.Should().NotBeNull();
        var samples = latencySamplesField!.GetValue(client) as IDictionary<string, List<double>>;
        samples.Should().NotBeNull();
        samples!.ContainsKey("nodeX").Should().BeFalse("ForgetNode should remove latency samples for nodeX");

        coordCache.ContainsKey("nodeX").Should().BeFalse("coord cache entry should be removed on erase");
    }
}
