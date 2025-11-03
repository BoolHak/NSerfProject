// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Managers;

/// <summary>
/// Manages member state with transaction pattern for atomic operations.
/// Thread-safe implementation using ReaderWriterLockSlim.
/// </summary>
internal class MemberManager : IMemberManager, IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Dictionary<string, MemberInfo> _members = [];

    /// <summary>
    /// Executes an operation under write lock with access to member state.
    /// </summary>
    public TResult ExecuteUnderLock<TResult>(Func<IMemberStateAccessor, TResult> operation)
    {
        _lock.EnterWriteLock();
        try
        {
            var accessor = new MemberStateAccessor(this);
            return operation(accessor);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Executes an operation under write lock with access to member state (no return value).
    /// </summary>
    public void ExecuteUnderLock(Action<IMemberStateAccessor> operation)
    {
        _lock.EnterWriteLock();
        try
        {
            var accessor = new MemberStateAccessor(this);
            operation(accessor);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Disposes the lock resources.
    /// </summary>
    public void Dispose()
    {
        _lock?.Dispose();
    }

    /// <summary>
    /// Internal accessor implementation - provides direct access to member state.
    /// Assumes caller already holds the lock.
    /// </summary>
    private class MemberStateAccessor(MemberManager manager) : IMemberStateAccessor
    {
        public MemberInfo? GetMember(string name)
        {
            return manager._members.TryGetValue(name, out var member) ? member : null;
        }

        public List<MemberInfo> GetAllMembers()
        {
            return [.. manager._members.Values];
        }

        public int GetMemberCount()
        {
            return manager._members.Count;
        }

        public void AddMember(MemberInfo member)
        {
            manager._members[member.Name] = member;
        }

        public void UpdateMember(string name, Action<MemberInfo> updater)
        {
            if (manager._members.TryGetValue(name, out var member))
            {
                updater(member);
            }
        }

        public bool RemoveMember(string name)
        {
            return manager._members.Remove(name);
        }

        public List<MemberInfo> GetFailedMembers()
        {
            return [.. manager._members.Values.Where(m => m.Status == MemberStatus.Failed)];
        }

        public List<MemberInfo> GetLeftMembers()
        {
            return [.. manager._members.Values.Where(m => m.Status == MemberStatus.Left)];
        }

        public List<MemberInfo> GetMembersByStatus(MemberStatus status)
        {
            return [.. manager._members.Values.Where(m => m.Status == status)];
        }
    }
}
