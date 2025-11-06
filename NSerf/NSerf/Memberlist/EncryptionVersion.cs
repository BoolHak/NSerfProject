// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Encryption version for message encryption.
/// </summary>
public enum EncryptionVersion : byte
{
    /// <summary>
    /// Version 0 uses PKCS7 padding.
    /// </summary>
    Version0 = 0,
    
    /// <summary>
    /// Version 1 does not use padding.
    /// </summary>
    Version1 = 1
}

/// <summary>
/// Helper methods for encryption version.
/// </summary>
public static class EncryptionVersionHelper
{
    /// <summary>
    /// Returns the encryption version to use based on a protocol version.
    /// </summary>
    public static EncryptionVersion GetEncryptionVersion(byte protocolVersion)
    {
        return protocolVersion == 1 ? EncryptionVersion.Version0 : EncryptionVersion.Version1;
    }
}
