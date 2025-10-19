// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Diagnostics;

namespace NSerf.Metrics;

/// <summary>
/// In-memory metrics implementation for testing.
/// Captures all metrics emissions and provides query methods.
/// </summary>
public sealed class InMemoryMetrics : IMetrics
{
    private readonly ConcurrentDictionary<string, float> _counters = new();
    private readonly ConcurrentDictionary<string, float> _gauges = new();
    private readonly ConcurrentBag<Sample> _samples = new();
    private readonly ConcurrentBag<Duration> _durations = new();

    public record Sample(string Key, float Value, MetricLabel[] Labels, DateTimeOffset Timestamp);
    public record Duration(string Key, TimeSpan Elapsed, MetricLabel[] Labels, DateTimeOffset Timestamp);

    /// <summary>
    /// Gets all counters with their current values.
    /// </summary>
    public IReadOnlyDictionary<string, float> Counters => _counters;

    /// <summary>
    /// Gets all gauges with their current values.
    /// </summary>
    public IReadOnlyDictionary<string, float> Gauges => _gauges;

    /// <summary>
    /// Gets all recorded samples.
    /// </summary>
    public IEnumerable<Sample> Samples => _samples;

    /// <summary>
    /// Gets all recorded durations.
    /// </summary>
    public IEnumerable<Duration> Durations => _durations;

    public void IncrCounter(string[] key, float value, MetricLabel[]? labels = null)
    {
        var keyStr = string.Join(".", key);
        var fullKey = BuildFullKey(keyStr, labels);
        _counters.AddOrUpdate(fullKey, value, (_, current) => current + value);
    }

    public void SetGauge(string[] key, float value, MetricLabel[]? labels = null)
    {
        var keyStr = string.Join(".", key);
        var fullKey = BuildFullKey(keyStr, labels);
        _gauges[fullKey] = value;
    }

    public void AddSample(string[] key, float value, MetricLabel[]? labels = null)
    {
        var keyStr = string.Join(".", key);
        _samples.Add(new Sample(keyStr, value, labels ?? Array.Empty<MetricLabel>(), DateTimeOffset.UtcNow));
    }

    public IDisposable MeasureSince(string[] key, MetricLabel[]? labels = null)
    {
        var keyStr = string.Join(".", key);
        return new Timer(keyStr, labels ?? Array.Empty<MetricLabel>(), _durations);
    }

    /// <summary>
    /// Gets counter value by key. Returns 0 if not found.
    /// </summary>
    public float GetCounter(string key, MetricLabel[]? labels = null)
    {
        var fullKey = BuildFullKey(key, labels);
        return _counters.TryGetValue(fullKey, out var value) ? value : 0f;
    }

    /// <summary>
    /// Gets gauge value by key. Returns 0 if not found.
    /// </summary>
    public float GetGauge(string key, MetricLabel[]? labels = null)
    {
        var fullKey = BuildFullKey(key, labels);
        return _gauges.TryGetValue(fullKey, out var value) ? value : 0f;
    }

    /// <summary>
    /// Gets samples matching a key prefix.
    /// </summary>
    public IEnumerable<Sample> GetSamples(string keyPrefix)
    {
        return _samples.Where(s => s.Key.StartsWith(keyPrefix));
    }

    /// <summary>
    /// Gets durations matching a key prefix.
    /// </summary>
    public IEnumerable<Duration> GetDurations(string keyPrefix)
    {
        return _durations.Where(d => d.Key.StartsWith(keyPrefix));
    }

    /// <summary>
    /// Resets all metrics.
    /// </summary>
    public void Reset()
    {
        _counters.Clear();
        _gauges.Clear();
        _samples.Clear();
        _durations.Clear();
    }

    private static string BuildFullKey(string key, MetricLabel[]? labels)
    {
        if (labels == null || labels.Length == 0)
            return key;

        var labelStr = string.Join(",", labels.Select(l => $"{l.Name}={l.Value}"));
        return $"{key}{{{labelStr}}}";
    }

    private sealed class Timer : IDisposable
    {
        private readonly string _key;
        private readonly MetricLabel[] _labels;
        private readonly ConcurrentBag<Duration> _durations;
        private readonly Stopwatch _stopwatch;

        public Timer(string key, MetricLabel[] labels, ConcurrentBag<Duration> durations)
        {
            _key = key;
            _labels = labels;
            _durations = durations;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _durations.Add(new Duration(_key, _stopwatch.Elapsed, _labels, DateTimeOffset.UtcNow));
        }
    }
}
