// Ported from: github.com/hashicorp/memberlist/security.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Calculates encryption overhead for messages.
/// </summary>
public static class EncryptionOverhead
{
    private const int VersionSize = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaxPadOverhead = 16;
    
    /// <summary>
    /// Returns the byte overhead of encryption for a given version.
    /// </summary>
    public static int GetOverhead(EncryptionVersion version)
    {
        return version switch
        {
            EncryptionVersion.Version0 => VersionSize + NonceSize + TagSize + MaxPadOverhead,
            EncryptionVersion.Version1 => VersionSize + NonceSize + TagSize,
            _ => 0
        };
    }
    
    /// <summary>
    /// Returns the total encrypted length for a message.
    /// </summary>
    public static int EncryptedLength(EncryptionVersion version, int messageLength)
    {
        return version switch
        {
            EncryptionVersion.Version0 => VersionSize + NonceSize + messageLength + TagSize + MaxPadOverhead,
            EncryptionVersion.Version1 => VersionSize + NonceSize + messageLength + TagSize,
            _ => messageLength
        };
    }
}
