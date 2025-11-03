// Ported from: github.com/hashicorp/memberlist/net_transport.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

namespace NSerf.Memberlist.Transport;

/// <summary>
/// Configuration for NetTransport.
/// </summary>
public class NetTransportConfig
{
    /// <summary>
    /// List of addresses to bind to for both TCP and UDP communications.
    /// </summary>
    public List<string> BindAddrs { get; set; } = ["0.0.0.0"];

    /// <summary>
    /// Port to listen on for each address.
    /// Use 0 to let the OS assign a port automatically.
    /// </summary>
    public int BindPort { get; set; }

    /// <summary>
    /// Logger for operator messages.
    /// </summary>
    public ILogger? Logger { get; set; }
}
