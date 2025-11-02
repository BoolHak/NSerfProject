// Ported from: github.com/hashicorp/memberlist/memberlist.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace NSerf.Memberlist;

/// <summary>
/// Resolves addresses for cluster nodes.
/// </summary>
public class AddressResolver(ILogger? logger = null)
{
    private readonly ILogger? _logger = logger;

    /// <summary>
    /// Resolves a host:port address to IP addresses.
    /// </summary>
    public async Task<List<IPEndPoint>> ResolveAsync(string address, int defaultPort, CancellationToken cancellationToken = default)
    {
        var results = new List<IPEndPoint>();

        // Try to parse as IP:port first
        if (address.Contains(':'))
        {
            var parts = address.Split(':');
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var ip) && int.TryParse(parts[1], out var port))
            {
                results.Add(new IPEndPoint(ip, port));
                return results;
            }
        }

        // Try to parse as IP only
        if (IPAddress.TryParse(address, out var ipAddr))
        {
            results.Add(new IPEndPoint(ipAddr, defaultPort));
            return results;
        }

        // Perform DNS resolution
        try
        {
            var hostEntry = await Dns.GetHostEntryAsync(address, cancellationToken);
            foreach (var addr in hostEntry.AddressList)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork ||
                    addr.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    results.Add(new IPEndPoint(addr, defaultPort));
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to resolve address {Address}", address);
        }

        return results;
    }
}
