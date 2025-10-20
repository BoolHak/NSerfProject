// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Text.RegularExpressions;

namespace NSerf.Serf.Helpers;

/// <summary>
/// Provides validation utilities for Serf configuration and parameters.
/// All methods are pure functions with no side effects.
/// </summary>
public static class SerfValidationHelper
{
    // Regex pattern to match invalid characters in node names
    // Valid characters are: A-Z, a-z, 0-9, dash (-), and dot (.)
    // Reference: Go serf.go line 1926
    private static readonly Regex InvalidNameRegex = new Regex(@"[^A-Za-z0-9\-\.]+", RegexOptions.Compiled);
    
    /// <summary>
    /// Validates a node name according to Serf requirements.
    /// Returns null if valid, or an error message if invalid.
    /// Reference: Go serf.go lines 1925-1933
    /// </summary>
    /// <param name="nodeName">Node name to validate</param>
    /// <param name="validateEnabled">Whether validation is enabled</param>
    /// <returns>Error message if invalid, null if valid</returns>
    public static string? ValidateNodeName(string nodeName, bool validateEnabled)
    {
        // Only validate if enabled
        if (!validateEnabled)
        {
            return null;
        }

        // Check for invalid characters (anything not alphanumeric, dash, or dot)
        if (InvalidNameRegex.IsMatch(nodeName))
        {
            return $"Node name contains invalid characters {nodeName}, Valid characters include " +
                   "all alpha-numerics and dashes and '.'";
        }

        // Check length limit (max 128 characters)
        if (nodeName.Length > 128)
        {
            return $"Node name is {nodeName.Length} characters. Node name must be 128 characters or less";
        }

        return null;
    }

    /// <summary>
    /// Validates the protocol version is within acceptable range.
    /// Throws ArgumentException if invalid.
    /// </summary>
    /// <param name="version">Protocol version to validate</param>
    /// <param name="minVersion">Minimum acceptable version</param>
    /// <param name="maxVersion">Maximum acceptable version</param>
    /// <exception cref="ArgumentException">Thrown when version is outside acceptable range</exception>
    public static void ValidateProtocolVersion(byte version, byte minVersion, byte maxVersion)
    {
        if (version < minVersion)
        {
            throw new ArgumentException(
                $"Protocol version '{version}' too low. Must be in range: [{minVersion}, {maxVersion}]");
        }
        
        if (version > maxVersion)
        {
            throw new ArgumentException(
                $"Protocol version '{version}' too high. Must be in range: [{minVersion}, {maxVersion}]");
        }
    }

    /// <summary>
    /// Validates the user event size limit is within acceptable bounds.
    /// Throws ArgumentException if invalid.
    /// </summary>
    /// <param name="configuredLimit">User-configured size limit</param>
    /// <param name="absoluteLimit">Absolute maximum size limit</param>
    /// <exception cref="ArgumentException">Thrown when limit exceeds absolute maximum</exception>
    public static void ValidateUserEventSizeLimit(int configuredLimit, int absoluteLimit)
    {
        if (configuredLimit > absoluteLimit)
        {
            throw new ArgumentException(
                $"User event size limit exceeds limit of {absoluteLimit} bytes");
        }
    }

    /// <summary>
    /// Validates user event size before and after encoding.
    /// Throws InvalidOperationException if size exceeds limits.
    /// </summary>
    /// <param name="name">Event name</param>
    /// <param name="payload">Event payload</param>
    /// <param name="configuredLimit">Configured size limit</param>
    /// <param name="absoluteLimit">Absolute size limit</param>
    /// <param name="encodedSize">Size after encoding (0 if not yet encoded)</param>
    /// <exception cref="InvalidOperationException">Thrown when size exceeds limits</exception>
    public static void ValidateUserEventSize(
        string name, 
        byte[] payload, 
        int configuredLimit, 
        int absoluteLimit,
        int encodedSize = 0)
    {
        var unEncodedSize = name.Length + payload.Length;

        // Check size before encoding
        if (encodedSize == 0)
        {
            if (unEncodedSize > configuredLimit)
            {
                throw new InvalidOperationException(
                    $"User event exceeds configured limit of {configuredLimit} bytes before encoding");
            }

            if (unEncodedSize > absoluteLimit)
            {
                throw new InvalidOperationException(
                    $"User event exceeds sane limit of {absoluteLimit} bytes before encoding");
            }
        }
        else
        {
            // Check size after encoding
            if (encodedSize > configuredLimit)
            {
                throw new InvalidOperationException(
                    $"Encoded user event exceeds configured limit of {configuredLimit} bytes after encoding");
            }

            if (encodedSize > absoluteLimit)
            {
                throw new InvalidOperationException(
                    $"Encoded user event exceeds reasonable limit of {absoluteLimit} bytes after encoding");
            }
        }
    }
}
