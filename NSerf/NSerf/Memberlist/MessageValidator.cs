// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

namespace NSerf.Memberlist;

/// <summary>
/// Validates incoming protocol messages.
/// </summary>
public class MessageValidator(ILogger? logger = null)
{
    private readonly ILogger? _logger = logger;

    /// <summary>
    /// Validates message size.
    /// </summary>
    public bool ValidateMessageSize(byte[] message, int maxSize)
    {
        if (message.Length > maxSize)
        {
            _logger?.LogWarning("Message too large: {Size} > {Max}", message.Length, maxSize);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Validates node name.
    /// </summary>
    public bool ValidateNodeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger?.LogWarning("Invalid node name: empty");
            return false;
        }

        if (name.Length > 255)
        {
            _logger?.LogWarning("Node name too long: {Length}", name.Length);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates incarnation number transition.
    /// </summary>
    public static bool ValidateIncarnation(uint oldInc, uint newInc)
    {
        return newInc >= oldInc;
    }
}
