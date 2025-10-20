using NSerf.Memberlist.State;

namespace NSerf.Serf.Handlers;

/// <summary>
/// Handles authoritative memberlist callbacks (NotifyJoin/NotifyLeave).
/// STATELESS - delegates all state management to MemberManager.
/// 
/// Key Difference from IntentHandler:
/// - IntentHandler: Processes gossip messages (limited authority)
/// - NodeEventHandler: Processes memberlist callbacks (AUTHORITATIVE - can resurrect Left/Failed)
/// 
/// Phase 4: Composition over inheritance - extracted from Serf.cs.
/// </summary>
internal interface INodeEventHandler
{
    /// <summary>
    /// Handles a memberlist NotifyJoin callback.
    /// AUTHORITATIVE: Can resurrect Left/Failed members.
    /// Always emits EventMemberJoin.
    /// </summary>
    void HandleNodeJoin(Node? node);
    
    /// <summary>
    /// Handles a memberlist NotifyLeave callback.
    /// AUTHORITATIVE: Transitions to Failed (dead) or Left (graceful).
    /// Emits EventMemberFailed or EventMemberLeave.
    /// </summary>
    void HandleNodeLeave(Node? node);
}
