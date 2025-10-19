// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Metrics abstraction layer, inspired by hashicorp/go-metrics

namespace NSerf.Metrics;

/// <summary>
/// Represents a metric label (key-value pair) for dimensional metrics.
/// </summary>
public readonly record struct MetricLabel(string Name, string Value);

/// <summary>
/// Interface for emitting metrics. Implementations can forward to Prometheus,
/// StatsD, Application Insights, or any other metrics system.
/// </summary>
public interface IMetrics
{
    /// <summary>
    /// Increments a counter by the specified value with optional labels.
    /// </summary>
    /// <param name="key">Metric name path (e.g., ["serf", "member", "join"])</param>
    /// <param name="value">Value to add to the counter</param>
    /// <param name="labels">Optional dimensional labels</param>
    void IncrCounter(string[] key, float value, MetricLabel[]? labels = null);

    /// <summary>
    /// Sets a gauge to a specific value with optional labels.
    /// </summary>
    /// <param name="key">Metric name path</param>
    /// <param name="value">Value to set</param>
    /// <param name="labels">Optional dimensional labels</param>
    void SetGauge(string[] key, float value, MetricLabel[]? labels = null);

    /// <summary>
    /// Records a sample/histogram value with optional labels.
    /// Used for distributions like message sizes or queue depths.
    /// </summary>
    /// <param name="key">Metric name path</param>
    /// <param name="value">Value to sample</param>
    /// <param name="labels">Optional dimensional labels</param>
    void AddSample(string[] key, float value, MetricLabel[]? labels = null);

    /// <summary>
    /// Measures duration since a start time. Returns an IDisposable that
    /// records the duration when disposed (useful with 'using' statement).
    /// </summary>
    /// <param name="key">Metric name path</param>
    /// <param name="labels">Optional dimensional labels</param>
    /// <returns>Disposable timer that records duration on dispose</returns>
    IDisposable MeasureSince(string[] key, MetricLabel[]? labels = null);
}

/// <summary>
/// Extension methods for convenient metric emission.
/// </summary>
public static class MetricsExtensions
{
    /// <summary>
    /// Increments a counter by 1.
    /// </summary>
    public static void IncrCounter(this IMetrics metrics, string[] key, MetricLabel[]? labels = null)
    {
        metrics.IncrCounter(key, 1.0f, labels);
    }

    /// <summary>
    /// Converts a string path like "serf.member.join" to string array ["serf", "member", "join"].
    /// </summary>
    public static string[] ToKeyArray(this string path)
    {
        return path.Split('.');
    }
}
