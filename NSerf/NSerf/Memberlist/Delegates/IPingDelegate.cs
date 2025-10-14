// Ported from: github.com/hashicorp/memberlist/ping_delegate.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist.Delegates;

/// <summary>
/// Used to notify an observer how long it took for a ping message to complete a round trip.
/// Can also be used for writing arbitrary byte slices into ack messages.
/// Note: In order to be meaningful for RTT estimates, this delegate does not apply to
/// indirect pings, nor fallback pings sent over TCP.
/// </summary>
public interface IPingDelegate
{
    /// <summary>
    /// Invoked when an ack is being sent; the returned bytes will be appended to the ack.
    /// </summary>
    /// <returns>Payload to append to ack message.</returns>
    byte[] AckPayload();
    
    /// <summary>
    /// Invoked when an ack for a ping is received.
    /// </summary>
    /// <param name="other">The node that responded.</param>
    /// <param name="rtt">Round-trip time for the ping.</param>
    /// <param name="payload">Payload received in the ack.</param>
    void NotifyPingComplete(Node other, TimeSpan rtt, ReadOnlySpan<byte> payload);
}
