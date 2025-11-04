// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Test helper extensions for accessing Serf internals

using NSerf.Serf;

namespace NSerfTests.Serf;

/// <summary>
/// Extension methods for testing Serf internals after MemberStates removal
/// </summary>
internal static class SerfTestExtensions
{
    /// <summary>
    /// Get a member from MemberManager for testing
    /// </summary>
    internal static MemberInfo? GetMember(this NSerf.Serf.Serf serf, string name)
    {
        return serf.MemberManager.ExecuteUnderLock(accessor => accessor.GetMember(name));
    }
    
    /// <summary>
    /// Check if a member exists in MemberManager for testing
    /// </summary>
    internal static bool HasMember(this NSerf.Serf.Serf serf, string name)
    {
        return serf.MemberManager.ExecuteUnderLock(accessor => accessor.GetMember(name)) != null;
    }
    
    /// <summary>
    /// Add a member directly to MemberManager for testing
    /// </summary>
    internal static void AddMember(this NSerf.Serf.Serf serf, MemberInfo memberInfo)
    {
        serf.MemberManager.ExecuteUnderLock(accessor => accessor.AddMember(memberInfo));
    }
    
    /// <summary>
    /// Get failed members for testing (replaces FailedMembers list)
    /// </summary>
    internal static List<MemberInfo> FailedMembers(this NSerf.Serf.Serf serf)
    {
        return serf.MemberManager.ExecuteUnderLock(accessor => accessor.GetFailedMembers());
    }
    
    /// <summary>
    /// Get left members for testing (replaces LeftMembers list)
    /// </summary>
    internal static List<MemberInfo> LeftMembers(this NSerf.Serf.Serf serf)
    {
        return serf.MemberManager.ExecuteUnderLock(accessor => accessor.GetLeftMembers());
    }
}
