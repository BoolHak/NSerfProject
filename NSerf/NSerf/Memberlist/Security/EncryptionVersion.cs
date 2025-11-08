// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Security;

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

