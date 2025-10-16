// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Version information for protocol and delegate versions.
/// </summary>
public class VersionInfo
{
    public byte ProtocolMin { get; set; }
    public byte ProtocolMax { get; set; }
    public byte ProtocolCur { get; set; }
    public byte DelegateMin { get; set; }
    public byte DelegateMax { get; set; }
    public byte DelegateCur { get; set; }
    
    /// <summary>
    /// Returns the version as a byte array for wire format.
    /// </summary>
    public byte[] ToByteArray()
    {
        return [ProtocolMin, ProtocolMax, ProtocolCur, DelegateMin, DelegateMax, DelegateCur];
    }
    
    /// <summary>
    /// Checks if this version is compatible with another.
    /// </summary>
    public bool IsCompatibleWith(VersionInfo other)
    {
        return ProtocolCur >= other.ProtocolMin && ProtocolCur <= other.ProtocolMax;
    }
}
