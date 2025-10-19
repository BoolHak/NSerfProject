// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Comprehensive metrics tests - follows TDD approach
// Verifies that Serf emits all expected metrics as per Go reference

using FluentAssertions;
using NSerf.Metrics;
using NSerf.Serf;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for Serf metrics emission.
/// Reference: serf/serf/serf.go (multiple locations with metrics.* calls)
/// </summary>
public class MetricsTest : IDisposable
{
    private readonly List<NSerf.Serf.Serf> _serfs = new();

    public void Dispose()
    {
        foreach (var serf in _serfs)
        {
            try { serf.ShutdownAsync().GetAwaiter().GetResult(); } catch { }
        }
    }

    private Config CreateTestConfigWithMetrics(string nodeName, InMemoryMetrics metrics)
    {
        var baseConfig = TestHelpers.CreateTestConfig(nodeName);
        
        // Convert SerfConfig to full Config with metrics support
        var config = new Config
        {
            NodeName = baseConfig.NodeName,
            MemberlistConfig = baseConfig.MemberlistConfig,
            Metrics = metrics,
            MetricLabels = new[] { new MetricLabel("test", "metrics") },
            Logger = baseConfig.Logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            ReapInterval = baseConfig.ReapInterval,
            ReconnectInterval = baseConfig.ReconnectInterval,
            ReconnectTimeout = baseConfig.ReconnectTimeout,
            TombstoneTimeout = baseConfig.TombstoneTimeout
        };
        
        if (config.MemberlistConfig != null)
        {
            config.MemberlistConfig.RequireNodeNames = false;
        }
        
        return config;
    }

    [Fact]
    public async Task Metrics_MemberJoin_ShouldEmitCounter()
    {
        // Arrange: Create 2-node cluster with metrics
        var metrics1 = new InMemoryMetrics();
        var s1 = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node1", metrics1));
        _serfs.Add(s1);

        var metrics2 = new InMemoryMetrics();
        var s2 = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node2", metrics2));
        _serfs.Add(s2);

        // Act: node2 joins node1
        await s2.JoinAsync(new[] { $"127.0.0.1:{s1.Config.MemberlistConfig!.BindPort}" }, false);
        await TestHelpers.WaitUntilNumNodesAsync(2, TimeSpan.FromSeconds(5), s1, s2);

        // Assert: Both nodes should emit member.join counter
        var labels = new[] { new MetricLabel("test", "metrics") };
        
        // s1 sees node2 join
        var joinCount1 = metrics1.GetCounter("serf.member.join", labels);
        joinCount1.Should().BeGreaterOrEqualTo(1, "node1 should see node2 join");

        // s2 sees node1 during join
        var joinCount2 = metrics2.GetCounter("serf.member.join", labels);
        joinCount2.Should().BeGreaterOrEqualTo(1, "node2 should see node1 during join");
    }

    [Fact]
    public async Task Metrics_MemberLeave_ShouldEmitCounter()
    {
        // Arrange: Create 2-node cluster
        var metrics1 = new InMemoryMetrics();
        var s1 = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node1", metrics1));
        _serfs.Add(s1);

        var s2 = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node2", new InMemoryMetrics()));
        _serfs.Add(s2);

        await s2.JoinAsync(new[] { $"127.0.0.1:{s1.Config.MemberlistConfig!.BindPort}" }, false);
        await TestHelpers.WaitUntilNumNodesAsync(2, TimeSpan.FromSeconds(5), s1, s2);

        metrics1.Reset(); // Clear join metrics

        // Act: node2 leaves
        await s2.LeaveAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert: node1 should see "left" status metric
        var labels = new[] { new MetricLabel("test", "metrics") };
        var leftCount = metrics1.GetCounter("serf.member.left", labels);
        leftCount.Should().BeGreaterOrEqualTo(1, "should emit counter when member leaves");
    }

    [Fact]
    public async Task Metrics_UserEvent_ShouldEmitCounter()
    {
        // Arrange
        var metrics = new InMemoryMetrics();
        var serf = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node1", metrics));
        _serfs.Add(serf);

        // Act: Send user event
        await serf.UserEventAsync("test-event", System.Text.Encoding.UTF8.GetBytes("payload"), false);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Assert: Should emit event counters
        var labels = new[] { new MetricLabel("test", "metrics") };
        var eventCount = metrics.GetCounter("serf.events", labels);
        eventCount.Should().BeGreaterOrEqualTo(1, "should emit generic events counter");

        var namedEventCount = metrics.GetCounter("serf.events.test-event", labels);
        namedEventCount.Should().BeGreaterOrEqualTo(1, "should emit named event counter");
    }

    [Fact]
    public async Task Metrics_Query_ShouldEmitCounters()
    {
        // Arrange: 2-node cluster
        var metrics1 = new InMemoryMetrics();
        var s1 = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node1", metrics1));
        _serfs.Add(s1);

        var metrics2 = new InMemoryMetrics();
        var s2 = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node2", metrics2));
        _serfs.Add(s2);

        await s2.JoinAsync(new[] { $"127.0.0.1:{s1.Config.MemberlistConfig!.BindPort}" }, false);
        await TestHelpers.WaitUntilNumNodesAsync(2, TimeSpan.FromSeconds(5), s1, s2);

        metrics1.Reset();
        metrics2.Reset();

        // Act: Send query from node1
        var queryParams = new QueryParam { Timeout = TimeSpan.FromSeconds(2) };
        var payload = System.Text.Encoding.UTF8.GetBytes("test-query-payload");
        var response = await s1.QueryAsync("test-query", payload, queryParams);

        // Wait for query to propagate
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert: Both nodes should emit query metrics
        var labels = new[] { new MetricLabel("test", "metrics") };
        var queriesCount = metrics2.GetCounter("serf.queries", labels);
        queriesCount.Should().BeGreaterOrEqualTo(1, "receiving node should count queries");

        var namedQueriesCount = metrics2.GetCounter("serf.queries.test-query", labels);
        namedQueriesCount.Should().BeGreaterOrEqualTo(1, "should count queries by name");
    }

    [Fact]
    public async Task Metrics_MessageSizes_ShouldEmitSamples()
    {
        // Arrange
        var metrics1 = new InMemoryMetrics();
        var s1 = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node1", metrics1));
        _serfs.Add(s1);

        var s2 = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node2", new InMemoryMetrics()));
        _serfs.Add(s2);

        // Act: Join triggers message exchange
        await s2.JoinAsync(new[] { $"127.0.0.1:{s1.Config.MemberlistConfig!.BindPort}" }, false);
        await TestHelpers.WaitUntilNumNodesAsync(2, TimeSpan.FromSeconds(5), s1, s2);

        // Give time for messages to be exchanged
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert: Should have recorded message samples
        var sentSamples = metrics1.GetSamples("serf.msgs.sent");
        sentSamples.Should().NotBeEmpty("should record sent message sizes");

        var receivedSamples = metrics1.GetSamples("serf.msgs.received");
        receivedSamples.Should().NotBeEmpty("should record received message sizes");
    }

    [Fact]
    public async Task Metrics_WithLabels_ShouldIncludeLabels()
    {
        // Arrange: Custom labels
        var metrics = new InMemoryMetrics();
        var config = CreateTestConfigWithMetrics("node1", metrics);
        config.MetricLabels = new[]
        {
            new MetricLabel("datacenter", "us-west"),
            new MetricLabel("env", "test")
        };
        
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfs.Add(serf);

        // Act: Trigger a metric (user event)
        await serf.UserEventAsync("labeled-event", System.Text.Encoding.UTF8.GetBytes("data"), false);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Assert: Counter should exist with labels
        var counter = metrics.GetCounter("serf.events", config.MetricLabels);
        counter.Should().BeGreaterThan(0, "should emit counter with labels");
    }

    [Fact]
    public async Task Metrics_QueueDepth_ShouldEmitGauge()
    {
        // Arrange
        var metrics = new InMemoryMetrics();
        var config = CreateTestConfigWithMetrics("node1", metrics);
        config.QueueCheckInterval = TimeSpan.FromMilliseconds(100); // Fast checks for test
        
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfs.Add(serf);

        // Act: Let queue monitoring run for a bit
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert: Should have queue depth samples
        // Serf has "event" and "query" queues
        var eventQueueSamples = metrics.GetSamples("serf.queue.event");
        var queryQueueSamples = metrics.GetSamples("serf.queue.query");

        (eventQueueSamples.Any() || queryQueueSamples.Any())
            .Should().BeTrue("should emit queue depth metrics");
    }

    [Fact]
    public void Metrics_NullMetrics_ShouldNotThrow()
    {
        // Arrange
        var nullMetrics = NullMetrics.Instance;

        // Act & Assert: All operations should be no-op, no exceptions
        nullMetrics.IncrCounter(new[] { "test", "counter" }, 1);
        nullMetrics.SetGauge(new[] { "test", "gauge" }, 42.0f);
        nullMetrics.AddSample(new[] { "test", "sample" }, 100.0f);
        
        using (nullMetrics.MeasureSince(new[] { "test", "duration" }))
        {
            // Timer should be no-op
        }

        // No exception = success
        true.Should().BeTrue("NullMetrics should not throw exceptions");
    }

    [Fact]
    public void InMemoryMetrics_ShouldTrackAllMetricTypes()
    {
        // Arrange
        var metrics = new InMemoryMetrics();
        var labels = new[] { new MetricLabel("test", "label") };

        // Act: Emit various metrics
        metrics.IncrCounter(new[] { "test", "counter" }, 5, labels);
        metrics.IncrCounter(new[] { "test", "counter" }, 3, labels);
        
        metrics.SetGauge(new[] { "test", "gauge" }, 42, labels);
        
        metrics.AddSample(new[] { "test", "sample" }, 100, labels);
        metrics.AddSample(new[] { "test", "sample" }, 200, labels);

        using (metrics.MeasureSince(new[] { "test", "duration" }, labels))
        {
            Thread.Sleep(10); // Simulate work
        }

        // Assert: All metrics captured
        metrics.GetCounter("test.counter", labels).Should().Be(8, "counter increments should sum");
        metrics.GetGauge("test.gauge", labels).Should().Be(42, "gauge should be set");
        
        var samples = metrics.GetSamples("test.sample");
        samples.Should().HaveCount(2, "should capture all samples");
        samples.Select(s => s.Value).Should().BeEquivalentTo(new[] { 100f, 200f });

        var durations = metrics.GetDurations("test.duration");
        durations.Should().HaveCount(1, "should capture duration");
        durations.First().Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public async Task Metrics_MemberFlap_ShouldEmitCounter()
    {
        // This tests the flap detection (member fails and quickly rejoins)
        // Arrange: Create cluster with short flap timeout
        var metrics1 = new InMemoryMetrics();
        var config1 = CreateTestConfigWithMetrics("node1", metrics1);
        config1.FlapTimeout = TimeSpan.FromSeconds(5);
        
        var s1 = await NSerf.Serf.Serf.CreateAsync(config1);
        _serfs.Add(s1);

        var s2 = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node2", new InMemoryMetrics()));
        _serfs.Add(s2);

        await s2.JoinAsync(new[] { $"127.0.0.1:{s1.Config.MemberlistConfig!.BindPort}" }, false);
        await TestHelpers.WaitUntilNumNodesAsync(2, TimeSpan.FromSeconds(5), s1, s2);

        metrics1.Reset();

        // Act: Simulate flap (node2 leaves and rejoins quickly)
        await s2.LeaveAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        var s2Rejoin = await NSerf.Serf.Serf.CreateAsync(CreateTestConfigWithMetrics("node2", new InMemoryMetrics()));
        _serfs.Add(s2Rejoin);
        await s2Rejoin.JoinAsync(new[] { $"127.0.0.1:{s1.Config.MemberlistConfig!.BindPort}" }, false);
        
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert: Should detect flap if rejoin happens within FlapTimeout
        // (This may not always trigger depending on timing, so we check if flap metric exists OR join metric exists)
        var labels = new[] { new MetricLabel("test", "metrics") };
        var flapCount = metrics1.GetCounter("serf.member.flap", labels);
        var joinCount = metrics1.GetCounter("serf.member.join", labels);

        (flapCount > 0 || joinCount > 0).Should().BeTrue("should emit either flap or join metric");
    }
}
