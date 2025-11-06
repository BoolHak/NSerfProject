// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/coordinate/client.go

namespace NSerf.Coordinate;

/// <summary>
/// CoordinateClient manages the estimated network coordinate for a given node and adjusts
/// it as the node observes round trip times and estimated coordinates from other nodes.
/// The core algorithm is based on Vivaldi.
/// </summary>
public class CoordinateClient
{
    private readonly object _mutex = new();
    private Coordinate _coord;
    private readonly Coordinate _origin;
    private readonly CoordinateConfig _config;
    private uint _adjustmentIndex;
    private readonly double[] _adjustmentSamples;
    private readonly Dictionary<string, List<double>> _latencyFilterSamples;
    private ClientStats _stats;

    /// <summary>
    /// Creates a new CoordinateClient and verifies the configuration is valid.
    /// </summary>
    public CoordinateClient(CoordinateConfig config)
    {
        if (config.Dimensionality == 0)
        {
            throw new ArgumentException("dimensionality must be >0", nameof(config));
        }

        _coord = Coordinate.NewCoordinate(config);
        _origin = Coordinate.NewCoordinate(config);
        _config = config;
        _adjustmentIndex = 0;
        _adjustmentSamples = new double[config.AdjustmentWindowSize];
        _latencyFilterSamples = [];
        _stats = new ClientStats();
    }

    /// <summary>
    /// Returns a copy of the coordinate for this client.
    /// </summary>
    public Coordinate GetCoordinate()
    {
        lock (_mutex)
        {
            return _coord.Clone();
        }
    }

    /// <summary>
    /// Forces the client's coordinate to a known state.
    /// </summary>
    public void SetCoordinate(Coordinate coord)
    {
        lock (_mutex)
        {
            CheckCoordinate(coord);
            _coord = coord.Clone();
        }
    }

    /// <summary>
    /// Removes any client state for the given node.
    /// </summary>
    public void ForgetNode(string node)
    {
        lock (_mutex)
        {
            _latencyFilterSamples.Remove(node);
        }
    }

    /// <summary>
    /// Returns a copy of stats for the client.
    /// </summary>
    public ClientStats Stats()
    {
        lock (_mutex)
        {
            return _stats;
        }
    }

    /// <summary>
    /// Returns an error if the coordinate isn't compatible with this client
    /// or if the coordinate itself isn't valid.
    /// </summary>
    private void CheckCoordinate(Coordinate coord)
    {
        if (!_coord.IsCompatibleWith(coord))
        {
            throw new ArgumentException("dimensions aren't compatible");
        }

        if (!coord.IsValid())
        {
            throw new ArgumentException("coordinate is invalid");
        }
    }

    /// <summary>
    /// Applies a simple moving median filter with a new sample for a node.
    /// </summary>
    private double LatencyFilter(string node, double rttSeconds)
    {
        if (!_latencyFilterSamples.TryGetValue(node, out var samples))
        {
            samples = new List<double>((int)_config.LatencyFilterSize);
            _latencyFilterSamples[node] = samples;
        }

        // Add the new sample and trim the list, if needed.
        samples.Add(rttSeconds);
        if (samples.Count > _config.LatencyFilterSize)
        {
            samples.RemoveAt(0);
        }

        // Sort a copy of the samples and return the median.
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    /// <summary>
    /// Updates the Vivaldi portion of the client's coordinate.
    /// </summary>
    private void UpdateVivaldi(Coordinate other, double rttSeconds)
    {
        const double zeroThreshold = 1.0e-6;

        var dist = _coord.DistanceTo(other).TotalSeconds;
        if (rttSeconds < zeroThreshold)
        {
            rttSeconds = zeroThreshold;
        }
        var wrongness = Math.Abs(dist - rttSeconds) / rttSeconds;

        var totalError = _coord.Error + other.Error;
        if (totalError < zeroThreshold)
        {
            totalError = zeroThreshold;
        }
        var weight = _coord.Error / totalError;

        _coord.Error = _config.VivaldiCE * weight * wrongness + _coord.Error * (1.0 - _config.VivaldiCE * weight);
        if (_coord.Error > _config.VivaldiErrorMax)
        {
            _coord.Error = _config.VivaldiErrorMax;
        }

        var delta = _config.VivaldiCC * weight;
        var force = delta * (rttSeconds - dist);
        _coord = _coord.ApplyForce(_config, force, other);
    }

    /// <summary>
    /// Updates the adjustment portion of the client's coordinate if the feature is enabled.
    /// </summary>
    private void UpdateAdjustment(Coordinate other, double rttSeconds)
    {
        if (_config.AdjustmentWindowSize == 0)
        {
            return;
        }

        // Note that the existing adjustment factors don't figure in to this
        // calculation, so we use the raw distance here.
        var dist = _coord.RawDistanceTo(other);
        _adjustmentSamples[_adjustmentIndex] = rttSeconds - dist;
        _adjustmentIndex = (_adjustmentIndex + 1) % _config.AdjustmentWindowSize;

        double sum = 0.0;
        foreach (var sample in _adjustmentSamples)
        {
            sum += sample;
        }
        _coord.Adjustment = sum / (2.0 * _config.AdjustmentWindowSize);
    }

    /// <summary>
    /// Applies a small amount of gravity to pull coordinates towards
    /// the center of the coordinate system to combat drift.
    /// </summary>
    private void UpdateGravity()
    {
        var dist = _origin.DistanceTo(_coord).TotalSeconds;
        var force = -1.0 * Math.Pow(dist / _config.GravityRho, 2.0);
        _coord = _coord.ApplyForce(_config, force, _origin);
    }

    /// <summary>
    /// Takes other, a coordinate for another node, and rtt, a round-trip
    /// time observation for a ping to that node, and updates the estimated position of
    /// the client's coordinate. Returns the updated coordinate.
    /// </summary>
    public Coordinate Update(string node, Coordinate other, TimeSpan rtt)
    {
        lock (_mutex)
        {
            CheckCoordinate(other);

            // Validate RTT
            var maxRtt = TimeSpan.FromSeconds(10);
            if (rtt < TimeSpan.Zero || rtt > maxRtt)
            {
                throw new ArgumentException(
                    $"round trip time not in valid range, duration {rtt} is not a positive value less than {maxRtt}");
            }

            // Note: In Go they track zero RTTs with metrics, we'll skip that for now

            var rttSeconds = LatencyFilter(node, rtt.TotalSeconds);
            UpdateVivaldi(other, rttSeconds);
            UpdateAdjustment(other, rttSeconds);
            UpdateGravity();

            if (_coord.IsValid()) return _coord.Clone();
            _stats.Resets++;
            _coord = Coordinate.NewCoordinate(_config);

            return _coord.Clone();
        }
    }

    /// <summary>
    /// Returns the estimated RTT from the client's coordinate to other, the coordinate for another node.
    /// </summary>
    public TimeSpan DistanceTo(Coordinate other)
    {
        lock (_mutex)
        {
            return _coord.DistanceTo(other);
        }
    }
}

/// <summary>
/// ClientStats is used to record events that occur when updating coordinates.
/// </summary>
public struct ClientStats
{
    /// <summary>
    /// Resets are incremented any time we reset our local coordinate because
    /// our calculations have resulted in an invalid state.
    /// </summary>
    public int Resets { get; set; }
}
