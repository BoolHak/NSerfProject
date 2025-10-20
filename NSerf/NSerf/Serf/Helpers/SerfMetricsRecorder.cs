// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Metrics;

namespace NSerf.Serf.Helpers;

/// <summary>
/// Provides metrics recording utilities for Serf message transmission.
/// Centralizes metrics emission for message send/receive operations.
/// </summary>
public class SerfMetricsRecorder
{
    private readonly IMetrics _metrics;
    private readonly MetricLabel[] _labels;
    private readonly ILogger? _logger;

    /// <summary>
    /// Creates a new SerfMetricsRecorder with the specified metrics sink and labels.
    /// </summary>
    /// <param name="metrics">Metrics interface to emit metrics to</param>
    /// <param name="labels">Labels to attach to all metrics</param>
    /// <param name="logger">Optional logger for trace logging</param>
    public SerfMetricsRecorder(
        IMetrics metrics,
        MetricLabel[] labels,
        ILogger? logger = null)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _labels = labels ?? throw new ArgumentNullException(nameof(labels));
        _logger = logger;
    }

    /// <summary>
    /// Records that a message was received.
    /// Emits metrics for message size.
    /// Reference: Go delegate.go:36
    /// </summary>
    /// <param name="size">Size of the received message in bytes</param>
    public void RecordMessageReceived(int size)
    {
        _metrics.AddSample(new[] { "serf", "msgs", "received" }, size, _labels);
        _logger?.LogTrace("[SerfMetrics] Message received: {Size} bytes", size);
    }

    /// <summary>
    /// Records that a message was sent.
    /// Emits metrics for message size.
    /// Reference: Go delegate.go:148
    /// </summary>
    /// <param name="size">Size of the sent message in bytes</param>
    public void RecordMessageSent(int size)
    {
        _metrics.AddSample(new[] { "serf", "msgs", "sent" }, size, _labels);
        _logger?.LogTrace("[SerfMetrics] Message sent: {Size} bytes", size);
    }

    /// <summary>
    /// Records a member join event.
    /// </summary>
    public void RecordMemberJoin()
    {
        _metrics.IncrCounter(new[] { "serf", "member", "join" }, 1, _labels);
    }

    /// <summary>
    /// Records a member status change (failed, left, etc).
    /// </summary>
    /// <param name="status">Member status string</param>
    public void RecordMemberStatus(string status)
    {
        _metrics.IncrCounter(new[] { "serf", "member", status }, 1, _labels);
    }

    /// <summary>
    /// Records a member update event.
    /// </summary>
    public void RecordMemberUpdate()
    {
        _metrics.IncrCounter(new[] { "serf", "member", "update" }, 1, _labels);
    }

    /// <summary>
    /// Records a member flap detection event.
    /// </summary>
    public void RecordMemberFlap()
    {
        _metrics.IncrCounter(new[] { "serf", "member", "flap" }, 1, _labels);
    }

    /// <summary>
    /// Records a user event emission.
    /// </summary>
    /// <param name="eventName">Name of the user event</param>
    public void RecordUserEvent(string eventName)
    {
        _metrics.IncrCounter(new[] { "serf", "events" }, 1, _labels);
        _metrics.IncrCounter(new[] { "serf", "events", eventName }, 1, _labels);
    }

    /// <summary>
    /// Records a coordinate adjustment.
    /// </summary>
    /// <param name="adjustmentMs">Adjustment in milliseconds</param>
    public void RecordCoordinateAdjustment(float adjustmentMs)
    {
        _metrics.AddSample(new[] { "serf", "coordinate", "adjustment-ms" }, adjustmentMs, _labels);
    }

    /// <summary>
    /// Records a coordinate rejection.
    /// </summary>
    public void RecordCoordinateRejection()
    {
        _metrics.IncrCounter(new[] { "serf", "coordinate", "rejected" }, 1, _labels);
    }
}
