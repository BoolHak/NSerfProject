// Ported from: github.com/hashicorp/memberlist/config.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Net.Sockets;

namespace NSerf.Memberlist.Configuration;

/// <summary>
/// Represents an IP network (CIDR notation).
/// </summary>
public class IPNetwork(IPAddress baseAddress, int prefixLength)
{
    /// <summary>
    /// Base address of the network.
    /// </summary>
    public IPAddress BaseAddress { get; } = baseAddress;

    /// <summary>
    /// Prefix length (number of bits in network mask).
    /// </summary>
    public int PrefixLength { get; } = prefixLength;

    /// <summary>
    /// Network mask.
    /// </summary>
    public IPAddress Mask { get; } = CreateMask(prefixLength, baseAddress.AddressFamily);

    /// <summary>
    /// Parses a CIDR notation string into an IPNetwork.
    /// </summary>
    public static IPNetwork Parse(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2)
        {
            throw new FormatException($"Invalid CIDR format: {cidr}");
        }

        var address = IPAddress.Parse(parts[0]);
        var prefixLength = int.Parse(parts[1]);

        return new IPNetwork(address, prefixLength);
    }

    /// <summary>
    /// Determines if the given IP address is contained within this network.
    /// </summary>
    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != BaseAddress.AddressFamily)
        {
            return false;
        }

        var baseBytes = BaseAddress.GetAddressBytes();
        var addrBytes = address.GetAddressBytes();
        var maskBytes = Mask.GetAddressBytes();

        return !baseBytes.Where((t, i) => (t & maskBytes[i]) != (addrBytes[i] & maskBytes[i])).Any();
    }

    private static IPAddress CreateMask(int prefixLength, AddressFamily family)
    {
        var totalBits = family == AddressFamily.InterNetwork ? 32 : 128;
        var byteCount = totalBits / 8;

        if (prefixLength < 0 || prefixLength > totalBits)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        }

        var maskBytes = new byte[byteCount];
        var remainingBits = prefixLength;

        for (var i = 0; i < byteCount; i++)
        {
            switch (remainingBits)
            {
                case >= 8:
                    maskBytes[i] = 0xFF;
                    remainingBits -= 8;
                    break;
                case > 0:
                    maskBytes[i] = (byte)(0xFF << (8 - remainingBits));
                    remainingBits = 0;
                    break;
                default:
                    maskBytes[i] = 0x00;
                    break;
            }
        }

        return new IPAddress(maskBytes);
    }

    public override string ToString()
    {
        return $"{BaseAddress}/{PrefixLength}";
    }
}
