// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Tracks metrics for individual nodes.
/// </summary>
public class NodeMetrics
{
    private long _probesSent;
    private long _probesReceived;
    private long _acksSent;
    private long _acksReceived;
    private long _indirectPings;
    private long _nacksSent;
    private long _nacksReceived;
    
    public long ProbesSent => Interlocked.Read(ref _probesSent);
    public long ProbesReceived => Interlocked.Read(ref _probesReceived);
    public long AcksSent => Interlocked.Read(ref _acksSent);
    public long AcksReceived => Interlocked.Read(ref _acksReceived);
    public long IndirectPings => Interlocked.Read(ref _indirectPings);
    public long NacksSent => Interlocked.Read(ref _nacksSent);
    public long NacksReceived => Interlocked.Read(ref _nacksReceived);
    
    public void IncrementProbesSent() => Interlocked.Increment(ref _probesSent);
    public void IncrementProbesReceived() => Interlocked.Increment(ref _probesReceived);
    public void IncrementAcksSent() => Interlocked.Increment(ref _acksSent);
    public void IncrementAcksReceived() => Interlocked.Increment(ref _acksReceived);
    public void IncrementIndirectPings() => Interlocked.Increment(ref _indirectPings);
    public void IncrementNacksSent() => Interlocked.Increment(ref _nacksSent);
    public void IncrementNacksReceived() => Interlocked.Increment(ref _nacksReceived);
}
