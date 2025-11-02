// Ported from: github.com/hashicorp/memberlist/logging.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Net.Sockets;

namespace NSerf.Memberlist.Common;

/// <summary>
/// Utility functions for logging network addresses and connections.
/// </summary>
public static class LoggingUtils
{
    /// <summary>
    /// Formats a network address for logging.
    /// </summary>
    public static string LogAddress(EndPoint? addr)
    {
        if (addr == null)
        {
            return "from=<unknown address>";
        }
        
        return $"from={addr}";
    }
    
    /// <summary>
    /// Formats a string address for logging.
    /// </summary>
    public static string LogStringAddress(string? addr)
    {
        if (string.IsNullOrEmpty(addr))
        {
            return "from=<unknown address>";
        }
        
        return $"from={addr}";
    }
    
    /// <summary>
    /// Formats a network stream connection for logging.
    /// </summary>
    public static string LogStream(NetworkStream? stream)
    {
        if (stream == null)
        {
            return LogAddress(null);
        }
        
        try
        {
            if (stream.Socket?.RemoteEndPoint != null)
            {
                return LogAddress(stream.Socket.RemoteEndPoint);
            }
        }
        catch
        {
            // Ignore errors getting remote endpoint
        }
        
        return LogAddress(null);
    }
}
