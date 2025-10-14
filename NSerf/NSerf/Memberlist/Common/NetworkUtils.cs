// Ported from: github.com/hashicorp/memberlist/util.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;

namespace NSerf.Memberlist.Common;

/// <summary>
/// Network utility functions for Memberlist.
/// </summary>
public static class NetworkUtils
{
    /// <summary>
    /// Joins a host and port into an address string suitable for use with a transport.
    /// Handles IPv6 addresses by wrapping them in brackets.
    /// </summary>
    /// <param name="host">The hostname or IP address.</param>
    /// <param name="port">The port number.</param>
    /// <returns>Formatted address string (e.g., "host:port" or "[::1]:port").</returns>
    public static string JoinHostPort(string host, ushort port)
    {
        // Check if this is an IPv6 address
        if (host.Contains(':') && !host.StartsWith('['))
        {
            return $"[{host}]:{port}";
        }
        
        return $"{host}:{port}";
    }
    
    /// <summary>
    /// Determines if the given string includes a port number.
    /// Handles IPv4, IPv6, and hostname formats.
    /// </summary>
    /// <param name="address">Address string to check.</param>
    /// <returns>True if the address includes a port, false otherwise.</returns>
    public static bool HasPort(string address)
    {
        // IPv6 address in brackets like [::1]:port
        if (address.StartsWith('['))
        {
            var lastBracket = address.LastIndexOf(']');
            var lastColon = address.LastIndexOf(':');
            return lastColon > lastBracket;
        }
        
        // For IPv4 or hostnames, a single colon indicates a port
        // IPv6 without brackets (count > 1) can't have a port
        return address.Count(c => c == ':') == 1;
    }
    
    /// <summary>
    /// Ensures the given string has a port number, appending the default port if necessary.
    /// </summary>
    /// <param name="address">Address string that may or may not have a port.</param>
    /// <param name="defaultPort">Default port to append if no port is present.</param>
    /// <returns>Address string guaranteed to have a port.</returns>
    public static string EnsurePort(string address, int defaultPort)
    {
        if (HasPort(address))
        {
            return address;
        }
        
        // If this is an IPv6 address, trim brackets before adding port
        // (JoinHostPort will add them back)
        var trimmed = address.Trim('[', ']');
        
        // For IPv6 addresses, use JoinHostPort to ensure proper formatting
        if (trimmed.Contains(':'))
        {
            return $"[{trimmed}]:{defaultPort}";
        }
        
        return $"{trimmed}:{defaultPort}";
    }
}
