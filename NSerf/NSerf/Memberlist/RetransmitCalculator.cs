// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Calculates retransmit limits for broadcast messages.
/// </summary>
public static class RetransmitCalculator
{
    /// <summary>
    /// Calculates how many times to retransmit a message.
    /// </summary>
    public static int CalculateRetransmits(int retransmitMult, int numNodes)
    {
        var nodeScale = Math.Ceiling(Math.Log10(numNodes + 1));
        return retransmitMult * (int)nodeScale;
    }
}
