// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Tracks network metrics for memberlist.
/// </summary>
public class NetworkMetrics
{
    private long _udpSent;
    private long _udpReceived;
    private long _tcpSent;
    private long _tcpReceived;
    private long _packetsDropped;
    
    public long UdpSent => Interlocked.Read(ref _udpSent);
    public long UdpReceived => Interlocked.Read(ref _udpReceived);
    public long TcpSent => Interlocked.Read(ref _tcpSent);
    public long TcpReceived => Interlocked.Read(ref _tcpReceived);
    public long PacketsDropped => Interlocked.Read(ref _packetsDropped);
    
    public void IncrementUdpSent(int bytes) => Interlocked.Add(ref _udpSent, bytes);
    public void IncrementUdpReceived(int bytes) => Interlocked.Add(ref _udpReceived, bytes);
    public void IncrementTcpSent(int bytes) => Interlocked.Add(ref _tcpSent, bytes);
    public void IncrementTcpReceived(int bytes) => Interlocked.Add(ref _tcpReceived, bytes);
    public void IncrementPacketsDropped() => Interlocked.Increment(ref _packetsDropped);
    
    public void Reset()
    {
        Interlocked.Exchange(ref _udpSent, 0);
        Interlocked.Exchange(ref _udpReceived, 0);
        Interlocked.Exchange(ref _tcpSent, 0);
        Interlocked.Exchange(ref _tcpReceived, 0);
        Interlocked.Exchange(ref _packetsDropped, 0);
    }
}
