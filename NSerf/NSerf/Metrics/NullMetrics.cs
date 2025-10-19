// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Metrics;

/// <summary>
/// No-op metrics implementation that discards all metrics.
/// Use this when metrics are not needed or disabled.
/// </summary>
public sealed class NullMetrics : IMetrics
{
    /// <summary>
    /// Singleton instance to avoid allocations.
    /// </summary>
    public static readonly NullMetrics Instance = new();

    private NullMetrics() { }

    public void IncrCounter(string[] key, float value, MetricLabel[]? labels = null)
    {
        // No-op
    }

    public void SetGauge(string[] key, float value, MetricLabel[]? labels = null)
    {
        // No-op
    }

    public void AddSample(string[] key, float value, MetricLabel[]? labels = null)
    {
        // No-op
    }

    public IDisposable MeasureSince(string[] key, MetricLabel[]? labels = null)
    {
        return NullTimer.Instance;
    }

    private sealed class NullTimer : IDisposable
    {
        public static readonly NullTimer Instance = new();
        private NullTimer() { }
        public void Dispose() { }
    }
}
