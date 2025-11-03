using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;
using NSerf.Serf.Events;
using NSerf.Serf.Managers;
using NSerf.Serf.StateMachine;

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
internal class NodeEventHandler(
    IMemberManager memberManager,
    List<IEvent> eventLog,
    LamportClock clock,
    ILogger? logger,
    Func<Dictionary<string, string>>? decodeTags) : INodeEventHandler
{
    private readonly IMemberManager _memberManager = memberManager ?? throw new ArgumentNullException(nameof(memberManager));
    private readonly List<IEvent> _eventLog = eventLog ?? throw new ArgumentNullException(nameof(eventLog));
    private readonly LamportClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger? _logger = logger;
    private readonly Func<Dictionary<string, string>>? _decodeTags = decodeTags;

    /// <summary>
    /// Handles a memberlist NotifyJoin callback.
    /// AUTHORITATIVE: Can resurrect Left/Failed members.
    /// Always emits EventMemberJoin.
    /// </summary>
    public void HandleNodeJoin(Node? node)
    {
        if (node == null)
        {
            _logger?.LogWarning("[NodeEventHandler] HandleNodeJoin called with null node");
            return;
        }

        _logger?.LogDebug("[NodeEventHandler] HandleNodeJoin: {Name}", node.Name);

        _memberManager.ExecuteUnderLock(accessor =>
        {
            // Create the full member object
            var member = new Member
            {
                Name = node.Name,
                Addr = node.Addr,
                Port = node.Port,
                Tags = _decodeTags?.Invoke() ?? new Dictionary<string, string>(),
                Status = MemberStatus.Alive,
                ProtocolMin = node.PMin,
                ProtocolMax = node.PMax,
                ProtocolCur = node.PCur,
                DelegateMin = node.DMin,
                DelegateMax = node.DMax,
                DelegateCur = node.DCur
            };

            // Update or create member state
            var memberInfo = accessor.GetMember(node.Name);
            if (memberInfo == null)
            {
                // New member - create
                accessor.AddMember(new MemberInfo
                {
                    Name = node.Name,
                    StateMachine = new MemberStateMachine(
                        node.Name,
                        MemberStatus.Alive,
                        _clock.Time(),
                        _logger),
                    Member = member
                });
                _logger?.LogInformation("[NodeEventHandler] NEW member joined: {Name}", node.Name);
            }
            else
            {
                // Existing member - update (rejoin/resurrection)
                accessor.UpdateMember(node.Name, m =>
                {
                    var result = m.StateMachine.TransitionOnMemberlistJoin();
                    m.Member = member;

                    _logger?.LogInformation("[NodeEventHandler] Member rejoined: {Name} ({Reason})",
                        node.Name, result.Reason);
                });
            }
        });

        // Emit EventMemberJoin
        var memberForEvent = new Member
        {
            Name = node.Name,
            Addr = node.Addr,
            Port = node.Port,
            Tags = _decodeTags?.Invoke() ?? [],
            Status = MemberStatus.Alive,
            ProtocolMin = node.PMin,
            ProtocolMax = node.PMax,
            ProtocolCur = node.PCur,
            DelegateMin = node.DMin,
            DelegateMax = node.DMax,
            DelegateCur = node.DCur
        };

        var memberEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member> { memberForEvent }
        };

        _eventLog.Add(memberEvent);
    }

    /// <summary>
    /// Handles a memberlist NotifyLeave callback.
    /// AUTHORITATIVE: Transitions to Failed (dead) or Left (graceful).
    /// Emits EventMemberFailed or EventMemberLeave.
    /// </summary>
    public void HandleNodeLeave(Node? node)
    {
        if (node == null)
        {
            _logger?.LogWarning("[NodeEventHandler] HandleNodeLeave called with null node");
            return;
        }

        _logger?.LogDebug("[NodeEventHandler] HandleNodeLeave: {Name}, NodeState={State}", node.Name, node.State);

        // Determine event type based on memberlist's determination
        // Memberlist sets State to Left for graceful leave, Dead for actual failure
        var eventType = EventType.MemberLeave;
        var memberStatus = MemberStatus.Left;

        if (node.State == NodeStateType.Dead)
        {
            // This is an actual failure, not a graceful leave
            eventType = EventType.MemberFailed;
            memberStatus = MemberStatus.Failed;
        }
        else if (node.State == NodeStateType.Left)
        {
            // This is a graceful leave
            eventType = EventType.MemberLeave;
            memberStatus = MemberStatus.Left;
        }

        _memberManager.ExecuteUnderLock(accessor =>
        {
            // Update member state if it exists
            var memberInfo = accessor.GetMember(node.Name);
            if (memberInfo != null)
            {
                // Store the full member info for reaper and reconnect
                var member = new Member
                {
                    Name = node.Name,
                    Addr = node.Addr,
                    Port = node.Port,
                    Tags = _decodeTags?.Invoke() ?? [],
                    Status = memberStatus,
                    ProtocolMin = node.PMin,
                    ProtocolMax = node.PMax,
                    ProtocolCur = node.PCur,
                    DelegateMin = node.DMin,
                    DelegateMax = node.DMax,
                    DelegateCur = node.DCur
                };

                accessor.UpdateMember(node.Name, m =>
                {
                    var isDead = (memberStatus == MemberStatus.Failed);
                    var result = m.StateMachine.TransitionOnMemberlistLeave(isDead);
                    m.LeaveTime = DateTimeOffset.UtcNow;
                    m.Member = member;

                    _logger?.LogDebug("[NodeEventHandler] {Reason}", result.Reason);
                });

                _logger?.LogInformation("[NodeEventHandler] Member {Status}: {Name}",
                    eventType == EventType.MemberFailed ? "failed" : "left", node.Name);

                // Emit event
                var memberEvent = new MemberEvent
                {
                    Type = eventType,
                    Members = [member]
                };

                _eventLog.Add(memberEvent);
            }
        });
    }
}
