// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/coordinate/coordinate.go

using MessagePack;

namespace NSerf.Coordinate;

/// <summary>
/// Coordinate is a specialized structure for holding network coordinates for the
/// Vivaldi-based coordinate mapping algorithm. All values in here are in units of seconds.
/// </summary>
[MessagePackObject]
public class Coordinate
{
    /// <summary>
    /// Vec is the Euclidean portion of the coordinate. This is used along
    /// with the other fields to provide an overall distance estimate. The
    /// units here are seconds.
    /// </summary>
    [Key(0)]
    public double[] Vec { get; set; } = Array.Empty<double>();

    /// <summary>
    /// Error reflects the confidence in the given coordinate and is updated
    /// dynamically by the Vivaldi Client. This is dimensionless.
    /// </summary>
    [Key(1)]
    public double Error { get; set; }

    /// <summary>
    /// Adjustment is a distance offset computed based on a calculation over
    /// observations from all other nodes over a fixed window and is updated
    /// dynamically by the Vivaldi Client. The units here are seconds.
    /// </summary>
    [Key(2)]
    public double Adjustment { get; set; }

    /// <summary>
    /// Height is a distance offset that accounts for non-Euclidean effects
    /// which model the access links from nodes to the core Internet.
    /// </summary>
    [Key(3)]
    public double Height { get; set; }

    // Constants
    private const double SecondsToNanoseconds = 1.0e9;
    private const double ZeroThreshold = 1.0e-6;

    /// <summary>
    /// Creates a new coordinate at the origin, using the given config to supply key initial values.
    /// </summary>
    public static Coordinate NewCoordinate(CoordinateConfig config)
    {
        return new Coordinate
        {
            Vec = new double[config.Dimensionality],
            Error = config.VivaldiErrorMax,
            Adjustment = 0.0,
            Height = config.HeightMin
        };
    }

    /// <summary>
    /// Creates an independent copy of this coordinate.
    /// </summary>
    public Coordinate Clone()
    {
        return new Coordinate
        {
            Vec = (double[])Vec.Clone(),
            Error = Error,
            Adjustment = Adjustment,
            Height = Height
        };
    }

    /// <summary>
    /// Returns false if a floating point value is a NaN or an infinity.
    /// </summary>
    private static bool ComponentIsValid(double f)
    {
        return !double.IsInfinity(f) && !double.IsNaN(f);
    }

    /// <summary>
    /// Returns false if any component of a coordinate isn't valid.
    /// </summary>
    public bool IsValid()
    {
        return Vec.All(ComponentIsValid) &&
           ComponentIsValid(Error) &&
           ComponentIsValid(Adjustment) &&
           ComponentIsValid(Height);
    }

    /// <summary>
    /// Checks to see if the two coordinates are compatible dimensionally.
    /// If this returns true then you are guaranteed to not get any runtime errors operating on them.
    /// </summary>
    public bool IsCompatibleWith(Coordinate other)
    {
        return Vec.Length == other.Vec.Length;
    }

    /// <summary>
    /// Returns the result of applying the force from the direction of the other coordinate.
    /// </summary>
    public Coordinate ApplyForce(CoordinateConfig config, double force, Coordinate other)
    {
        if (!IsCompatibleWith(other))
        {
            throw new DimensionalityConflictException();
        }

        var ret = Clone();
        var (unit, mag) = UnitVectorAt(Vec, other.Vec);
        ret.Vec = Add(ret.Vec, Mul(unit, force));

        if (mag > ZeroThreshold)
        {
            ret.Height = (ret.Height + other.Height) * force / mag + ret.Height;
            ret.Height = Math.Max(ret.Height, config.HeightMin);
        }

        return ret;
    }

    /// <summary>
    /// Returns the distance between this coordinate and the other coordinate, including adjustments.
    /// </summary>
    public TimeSpan DistanceTo(Coordinate other)
    {
        if (!IsCompatibleWith(other))
        {
            throw new DimensionalityConflictException();
        }

        var dist = RawDistanceTo(other);
        var adjustedDist = dist + Adjustment + other.Adjustment;
        if (adjustedDist > 0.0)
        {
            dist = adjustedDist;
        }

        // Handle NaN/Infinity - return zero if invalid
        if (double.IsNaN(dist) || double.IsInfinity(dist) || dist < 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(dist);
    }

    /// <summary>
    /// Returns the Vivaldi distance between this coordinate and the
    /// other coordinate in seconds, not including adjustments.
    /// </summary>
    internal double RawDistanceTo(Coordinate other)
    {
        return Magnitude(Diff(Vec, other.Vec)) + Height + other.Height;
    }

    // Vector operations

    /// <summary>
    /// Returns the sum of vec1 and vec2.
    /// </summary>
    private static double[] Add(double[] vec1, double[] vec2)
    {
        var ret = new double[vec1.Length];
        for (int i = 0; i < ret.Length; i++)
        {
            ret[i] = vec1[i] + vec2[i];
        }
        return ret;
    }

    /// <summary>
    /// Returns the difference between vec1 and vec2.
    /// </summary>
    private static double[] Diff(double[] vec1, double[] vec2)
    {
        var ret = new double[vec1.Length];
        for (int i = 0; i < ret.Length; i++)
        {
            ret[i] = vec1[i] - vec2[i];
        }
        return ret;
    }

    /// <summary>
    /// Returns vec multiplied by a scalar factor.
    /// </summary>
    private static double[] Mul(double[] vec, double factor)
    {
        var ret = new double[vec.Length];
        for (int i = 0; i < vec.Length; i++)
        {
            ret[i] = vec[i] * factor;
        }
        return ret;
    }

    /// <summary>
    /// Computes the magnitude of the vec.
    /// </summary>
    private static double Magnitude(double[] vec)
    {
        double sum = 0.0;
        foreach (var component in vec)
        {
            sum += component * component;
        }
        return Math.Sqrt(sum);
    }

    /// <summary>
    /// Returns a unit vector pointing at vec1 from vec2. If the two
    /// positions are the same then a random unit vector is returned.
    /// Also returns the distance between the points.
    /// </summary>
    private static (double[] unitVector, double magnitude) UnitVectorAt(double[] vec1, double[] vec2)
    {
        var ret = Diff(vec1, vec2);

        // If the coordinates aren't on top of each other we can normalize.
        var mag = Magnitude(ret);
        if (mag > ZeroThreshold)
        {
            return (Mul(ret, 1.0 / mag), mag);
        }

        // Otherwise, just return a random unit vector.
        var random = new Random();
        for (int i = 0; i < ret.Length; i++)
        {
            ret[i] = random.NextDouble() - 0.5;
        }

        mag = Magnitude(ret);
        if (mag > ZeroThreshold)
        {
            return (Mul(ret, 1.0 / mag), 0.0);
        }

        // And finally just give up and make a unit vector along the first dimension.
        // This should be exceedingly rare.
        ret = new double[ret.Length];
        ret[0] = 1.0;
        return (ret, 0.0);
    }
}

/// <summary>
/// Exception thrown when you try to perform operations with incompatible dimensions.
/// </summary>
public class DimensionalityConflictException : Exception
{
    public DimensionalityConflictException()
        : base("coordinate dimensionality does not match")
    {
    }
}
