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
    private readonly Dictionary<string, MemberInfo> _members = new();
    
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
    private class MemberStateAccessor : IMemberStateAccessor
    {
        private readonly MemberManager _manager;
        
        public MemberStateAccessor(MemberManager manager)
        {
            _manager = manager;
        }
        
        public MemberInfo? GetMember(string name)
        {
            return _manager._members.TryGetValue(name, out var member) ? member : null;
        }
        
        public List<MemberInfo> GetAllMembers()
        {
            return _manager._members.Values.ToList();
        }
        
        public int GetMemberCount()
        {
            return _manager._members.Count;
        }
        
        public void AddMember(MemberInfo member)
        {
            _manager._members[member.Name] = member;
        }
        
        public void UpdateMember(string name, Action<MemberInfo> updater)
        {
            if (_manager._members.TryGetValue(name, out var member))
            {
                updater(member);
            }
        }
        
        public bool RemoveMember(string name)
        {
            return _manager._members.Remove(name);
        }
        
        public List<MemberInfo> GetFailedMembers()
        {
            return _manager._members.Values
                .Where(m => m.Status == MemberStatus.Failed)
                .ToList();
        }
        
        public List<MemberInfo> GetLeftMembers()
        {
            return _manager._members.Values
                .Where(m => m.Status == MemberStatus.Left)
                .ToList();
        }
        
        public List<MemberInfo> GetMembersByStatus(MemberStatus status)
        {
            return _manager._members.Values
                .Where(m => m.Status == status)
                .ToList();
        }
    }
}
