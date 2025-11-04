// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/coordinate/config.go

namespace NSerf.Coordinate;

/// <summary>
/// CoordinateConfig is used to set the parameters of the Vivaldi-based coordinate mapping algorithm.
/// 
/// References:
/// [1] Dabek, Frank, et al. "Vivaldi: A decentralized network coordinate system."
///     ACM SIGCOMM Computer Communication Review. Vol. 34. No. 4. ACM, 2004.
/// [2] Ledlie, Jonathan, Paul Gardner, and Margo I. Seltzer. "Network Coordinates in the Wild." NSDI. Vol. 7. 2007.
/// [3] Lee, Sanghwan, et al. "On suitability of Euclidean embedding for host-based network coordinate systems."
///     Networking, IEEE/ACM Transactions on 18.1 (2010): 27-40.
/// </summary>
public class CoordinateConfig
{
    /// <summary>
    /// The dimensionality of the coordinate system. As discussed in [2], more
    /// dimensions improves the accuracy of the estimates up to a point. Per [2]
    /// we chose 8 dimensions plus a non-Euclidean height.
    /// </summary>
    public uint Dimensionality { get; set; } = 8;

    /// <summary>
    /// VivaldiErrorMax is the default error value when a node hasn't yet made
    /// any observations. It also serves as an upper limit on the error value in
    /// case observations cause the error value to increase without bound.
    /// </summary>
    public double VivaldiErrorMax { get; set; } = 1.5;

    /// <summary>
    /// VivaldiCE is a tuning factor that controls the maximum impact an
    /// observation can have on a node's confidence. See [1] for more details.
    /// </summary>
    public double VivaldiCE { get; set; } = 0.25;

    /// <summary>
    /// VivaldiCC is a tuning factor that controls the maximum impact an
    /// observation can have on a node's coordinate. See [1] for more details.
    /// </summary>
    public double VivaldiCC { get; set; } = 0.25;

    /// <summary>
    /// AdjustmentWindowSize is a tuning factor that determines how many samples
    /// we retain to calculate the adjustment factor as discussed in [3]. Setting
    /// this to zero disables this feature.
    /// </summary>
    public uint AdjustmentWindowSize { get; set; } = 20;

    /// <summary>
    /// HeightMin is the minimum value of the height parameter. Since this
    /// always must be positive, it will introduce a small amount error, so
    /// the chosen value should be relatively small compared to "normal" coordinates.
    /// </summary>
    public double HeightMin { get; set; } = 10.0e-6;

    /// <summary>
    /// LatencyFilterSize is the maximum number of samples that are retained
    /// per node, to compute a median. The intent is to ride out blips
    /// but still keep the delay low. See [2] for more details.
    /// </summary>
    public uint LatencyFilterSize { get; set; } = 3;

    /// <summary>
    /// GravityRho is a tuning factor that sets how much gravity has an effect
    /// to try to re-center coordinates. See [2] for more details.
    /// </summary>
    public double GravityRho { get; set; } = 150.0;

    /// <summary>
    /// Returns a CoordinateConfig that has some default values suitable for
    /// basic testing of the algorithm but not tuned to any particular type of cluster.
    /// </summary>
    public static CoordinateConfig DefaultConfig()
    {
        return new CoordinateConfig
        {
            Dimensionality = 8,
            VivaldiErrorMax = 1.5,
            VivaldiCE = 0.25,
            VivaldiCC = 0.25,
            AdjustmentWindowSize = 20,
            HeightMin = 10.0e-6,
            LatencyFilterSize = 3,
            GravityRho = 150.0
        };
    }
}
