// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Managers;

/// <summary>
/// Provides direct access to member state within a transaction.
/// Caller must already hold the appropriate lock via IMemberManager.ExecuteUnderLock.
/// </summary>
internal interface IMemberStateAccessor
{
    /// <summary>
    /// Gets a member by name. Returns null if not found.
    /// </summary>
    /// <param name="name">Member name</param>
    /// <returns>MemberInfo or null</returns>
    MemberInfo? GetMember(string name);
    
    /// <summary>
    /// Gets all members (active, leaving, failed, left).
    /// </summary>
    /// <returns>List of all members</returns>
    List<MemberInfo> GetAllMembers();
    
    /// <summary>
    /// Gets the count of all members.
    /// </summary>
    /// <returns>Total member count</returns>
    int GetMemberCount();
    
    /// <summary>
    /// Adds a new member to the collection.
    /// </summary>
    /// <param name="member">Member to add</param>
    void AddMember(MemberInfo member);
    
    /// <summary>
    /// Updates an existing member using the provided action.
    /// </summary>
    /// <param name="name">Member name</param>
    /// <param name="updater">Action to update the member</param>
    void UpdateMember(string name, Action<MemberInfo> updater);
    
    /// <summary>
    /// Removes a member from all collections.
    /// </summary>
    /// <param name="name">Member name</param>
    /// <returns>True if removed, false if not found</returns>
    bool RemoveMember(string name);
    
    /// <summary>
    /// Gets all failed members.
    /// </summary>
    /// <returns>List of failed members</returns>
    List<MemberInfo> GetFailedMembers();
    
    /// <summary>
    /// Gets all left members.
    /// </summary>
    /// <returns>List of left members</returns>
    List<MemberInfo> GetLeftMembers();
    
    /// <summary>
    /// Gets members filtered by status.
    /// </summary>
    /// <param name="status">Status to filter by</param>
    /// <returns>List of members with matching status</returns>
    List<MemberInfo> GetMembersByStatus(MemberStatus status);
}
