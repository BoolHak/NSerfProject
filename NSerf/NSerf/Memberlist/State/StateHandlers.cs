// Ported from: github.com/hashicorp/memberlist/state.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Common;
using NSerf.Memberlist.Messages;

namespace NSerf.Memberlist.State;

/// <summary>
/// Handles state transitions for nodes in the memberlist (alive, suspect, dead).
/// Corresponds to state.go in the Go implementation.
/// </summary>
public class StateHandlers(Memberlist memberlist, ILogger? logger)
{

    /// <summary>
    /// Invoked when we get a message about a live node.
    /// Corresponds to aliveNode() in state.go (lines 943-1156).
    /// </summary>
    public void HandleAliveNode(Alive alive, bool bootstrap, TaskCompletionSource<bool>? notify = null)
    {
        lock (memberlist.NodeLock)
        {
            var state = memberlist.NodeMap.GetValueOrDefault(alive.Node);

            // Don't process if we've left and this is about us
            if (memberlist.IsLeaving && alive.Node == memberlist.Config.Name)
            {
                return;
            }

            // Validate protocol versions (vsn[0]=pmin, vsn[1]=pmax, vsn[2]=pcur)
            if (alive.Vsn is { Length: >= 3 })
            {
                var pMin = alive.Vsn[0];
                var pMax = alive.Vsn[1];
                var pCur = alive.Vsn[2];

                if (pMin == 0 || pMax == 0 || pMin > pMax)
                {
                    var ip = new IPAddress(alive.Addr);
                    logger?.LogWarning("Ignoring alive message for '{Node}' ({IP}:{Port}) - invalid protocol versions: {Min} <= {Cur} <= {Max}",
                        alive.Node, ip, alive.Port, pMin, pCur, pMax);
                    return;
                }
            }

            // Invoke Alive delegate for custom filtering
            if (memberlist.Config.Alive != null)
            {
                if (alive.Vsn == null || alive.Vsn.Length < 6)
                {
                    var ip = new IPAddress(alive.Addr);
                    logger?.LogWarning("Ignoring alive message for '{Node}' ({IP}:{Port}) - missing version array",
                        alive.Node, ip, alive.Port);
                    return;
                }

                var node = new Node
                {
                    Name = alive.Node,
                    Addr = new IPAddress(alive.Addr),
                    Port = alive.Port,
                    Meta = alive.Meta ?? [],
                    PMin = alive.Vsn[0],
                    PMax = alive.Vsn[1],
                    PCur = alive.Vsn[2],
                    DMin = alive.Vsn[3],
                    DMax = alive.Vsn[4],
                    DCur = alive.Vsn[5]
                };

                try
                {
                    memberlist.Config.Alive.NotifyAlive(node);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Ignoring alive message for '{Node}' - delegate rejected: {Error}",
                        alive.Node, ex.Message);
                    return;
                }
            }

            var updatesNode = false;
            var isNew = state == null;

            // Create a new node if we've never seen it
            if (isNew)
            {
                // Check the IP allowlist
                if (memberlist.Config.CIDRsAllowed.Count > 0)
                {
                    var ip = new IPAddress(alive.Addr);
                    var allowed = memberlist.Config.CIDRsAllowed.Any(network => network.Contains(ip));

                    if (!allowed)
                    {
                        logger?.LogWarning("Rejected node {Node} ({IP}): not in allowed CIDR list", alive.Node, ip);
                        return;
                    }
                }

                state = new NodeState
                {
                    Node = new Node
                    {
                        Name = alive.Node,
                        Addr = new IPAddress(alive.Addr),
                        Port = alive.Port,
                        Meta = alive.Meta ?? []
                    },
                    State = NodeStateType.Dead, // Start as dead, will be set to alive below
                    Incarnation = 0,
                    StateChange = DateTimeOffset.UtcNow
                };

                // Set protocol versions if available
                if (alive.Vsn != null && alive.Vsn.Length >= 6)
                {
                    state.Node.PMin = alive.Vsn[0];
                    state.Node.PMax = alive.Vsn[1];
                    state.Node.PCur = alive.Vsn[2];
                    state.Node.DMin = alive.Vsn[3];
                    state.Node.DMax = alive.Vsn[4];
                    state.Node.DCur = alive.Vsn[5];
                }

                // Add to map
                memberlist.NodeMap[alive.Node] = state;

                // Add to nodes list at random position (for better failure detection distribution)
                var n = memberlist.Nodes.Count;
                var offset = MemberlistMath.RandomOffset(n);

                memberlist.Nodes.Add(state);
                if (offset < n)
                {
                    // Swap with element at offset
                    (memberlist.Nodes[offset], memberlist.Nodes[n]) = (memberlist.Nodes[n], memberlist.Nodes[offset]);
                }

                // Update node count
                Interlocked.Increment(ref memberlist.NumNodes);
            }
            else
            {
                // Check for address/port changes
                var addressChanged = !state!.Node!.Addr!.GetAddressBytes().SequenceEqual(alive.Addr);
                var portChanged = state.Node.Port != alive.Port;
                if (addressChanged || portChanged)
                {
                    // Check IP allowlist for new address
                    if (memberlist.Config.CIDRsAllowed.Count > 0)
                    {
                        var ip = new IPAddress(alive.Addr);
                        var allowed = memberlist.Config.CIDRsAllowed.Any(network => network.Contains(ip));

                        if (!allowed)
                        {
                            logger?.LogWarning("Rejected IP update for node {Node} from {OldIP} to {NewIP}: not in allowed CIDR list",
                                alive.Node, state.Node.Addr, ip);
                            return;
                        }
                    }

                    // Allow address update if:
                    // 1. Node is Left, OR
                    // 2. Node is Dead and reclaim time elapsed, OR
                    // 3. Incarnation is strictly greater (rejoin scenario)
                    var canReclaim = memberlist.Config.DeadNodeReclaimTime > TimeSpan.Zero &&
                                     (DateTimeOffset.UtcNow - state.StateChange) > memberlist.Config.DeadNodeReclaimTime;
                    var higherIncarnation = alive.Incarnation > state.Incarnation;

                    if (state.State == NodeStateType.Left || (state.State == NodeStateType.Dead && canReclaim) || higherIncarnation)
                    {
                        var newIp = new IPAddress(alive.Addr);
                        logger?.LogInformation("Updating address for left/failed node {Node} from {OldIP}:{OldPort} to {NewIP}:{NewPort}",
                            state.Node.Name, state.Node.Addr, state.Node.Port, newIp, alive.Port);
                        updatesNode = true;
                    }
                    else
                    {
                        // Conflict - same name, different address
                        var newIp = new IPAddress(alive.Addr);
                        logger?.LogError("Conflicting address for {Node}. Ours: {OurIP}:{OurPort} Theirs: {TheirIP}:{TheirPort} State: {State}",
                            state.Node.Name, state.Node.Addr, state.Node.Port, newIp, alive.Port, state.State);

                        // Notify conflict delegate
                        if (memberlist.Config.Conflict == null) return;
                        var otherNode = new Node
                        {
                            Name = alive.Node,
                            Addr = new IPAddress(alive.Addr),
                            Port = alive.Port,
                            Meta = alive.Meta ?? []
                        };
                        memberlist.Config.Conflict.NotifyConflict(state.Node, otherNode);
                        return;
                    }
                }
            }

            // Check incarnation numbers
            var isLocalNode = state.Node.Name == memberlist.Config.Name;

            // Allow Left nodes to rejoin with ANY incarnation (even lower) - they've been removed
            // Bail if the incarnation is older (strict less than), UNLESS the node is Left (rejoining)
            if (alive.Incarnation < state.Incarnation && !isLocalNode && !updatesNode && state.State != NodeStateType.Left)
            {
                return;
            }

            // Bail if equal incarnation and not a new node, not local, and not updating
            if (alive.Incarnation == state.Incarnation && !isNew && !isLocalNode && !updatesNode)
            {
                return;
            }

            // Bail if strictly less and this is about us
            if (alive.Incarnation < state.Incarnation && isLocalNode)
            {
                return;
            }

            // Clear any suspicion timer
            if (memberlist.NodeTimers.TryRemove(alive.Node, out var timerObj) && timerObj is Suspicion suspicion)
            {
                try
                {
                    suspicion.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore
                }


            }

            // Store old state for notification
            var oldState = state.State;
            var oldMeta = state.Node.Meta;

            // If this is us, we may need to refute
            if (!bootstrap && isLocalNode)
            {
                // Build version vector
                byte[] versions =
                [
                    state.Node.PMin, state.Node.PMax, state.Node.PCur,
                    state.Node.DMin, state.Node.DMax, state.Node.DCur
                ];

                // If same incarnation, check if values match
                if (alive.Incarnation == state.Incarnation &&
                    (alive.Meta?.SequenceEqual(state.Node.Meta) ?? state.Node.Meta.Length == 0) &&
                    (alive.Vsn?.SequenceEqual(versions) ?? versions.Length == 0))
                {
                    return; // Exact match, ignore
                }

                // Refute if incarnation is higher OR if the same incarnation with different meta/version
                // This handles the case where the cluster has a higher incarnation for us (e.g., after restart)
                RefuteNode(state, alive.Incarnation);
                var ip = new IPAddress(alive.Addr);

                if (alive.Incarnation > state.Incarnation)
                {
                    logger?.LogWarning("Refuting alive message for '{Node}' ({IP}:{Port}) - cluster has higher incarnation {ClusterInc} vs our {OurInc}",
                        alive.Node, ip, alive.Port, alive.Incarnation, state.Incarnation);
                }
                else
                {
                    logger?.LogWarning("Refuting alive message for '{Node}' ({IP}:{Port}) - meta or version mismatch",
                        alive.Node, ip, alive.Port);
                }
            }
            else
            {
                // Not us, broadcast it (unless in bootstrap mode)
                if (!bootstrap)
                {
                    memberlist.EncodeAndBroadcast(alive.Node, Messages.MessageType.Alive, alive);
                }
                notify?.TrySetResult(true); // Signal completion immediately for now

                // Update protocol versions
                if (alive.Vsn is { Length: >= 6 })
                {
                    state.Node.PMin = alive.Vsn[0];
                    state.Node.PMax = alive.Vsn[1];
                    state.Node.PCur = alive.Vsn[2];
                    state.Node.DMin = alive.Vsn[3];
                    state.Node.DMax = alive.Vsn[4];
                    state.Node.DCur = alive.Vsn[5];
                }

                // Update state
                state.Incarnation = alive.Incarnation;
                state.Node.Meta = alive.Meta ?? [];
                state.Node.Addr = new IPAddress(alive.Addr);
                state.Node.Port = alive.Port;

                if (state.State != NodeStateType.Alive)
                {
                    state.State = NodeStateType.Alive;
                    state.StateChange = DateTimeOffset.UtcNow;
                }
            }

            // Notify event delegate
            if (memberlist.Config.Events == null) return;
            if (oldState is NodeStateType.Dead or NodeStateType.Left)
            {
                // Dead/Left -> Alive = Join
                memberlist.Config.Events.NotifyJoin(state.Node);
            }
            else if (!oldMeta.SequenceEqual(state.Node.Meta))
            {
                // Meta changed = Update
                memberlist.Config.Events.NotifyUpdate(state.Node);
            }
        }
    }

    /// <summary>
    /// Invoked when we get a message about a suspect node.
    /// Corresponds to suspectNode() in state.go (lines 1160-1249).
    /// </summary>
    public void HandleSuspectNode(Suspect suspect)
    {
        lock (memberlist.NodeLock)
        {
            if (!memberlist.NodeMap.TryGetValue(suspect.Node, out var state))
            {
                // Never heard of this node, ignore
                return;
            }

            // Ignore old incarnation
            if (suspect.Incarnation < state.Incarnation)
            {
                return;
            }

            // Check if there's an existing suspicion timer we can confirm
            if (memberlist.NodeTimers.TryGetValue(suspect.Node, out var timerObj))
            {
                if (timerObj is Suspicion existingSuspicion && existingSuspicion.Confirm(suspect.From))
                {
                    // New confirmation, re-broadcast
                    memberlist.EncodeAndBroadcast(suspect.Node, Messages.MessageType.Suspect, suspect);
                }
                return;
            }

            // Ignore if not alive
            if (state.State != NodeStateType.Alive)
            {
                return;
            }

            // If this is us, refute it
            if (state.Node.Name == memberlist.Config.Name)
            {
                RefuteNode(state, suspect.Incarnation);
                logger?.LogWarning("Refuting suspect message from {From}", suspect.From);
                return; // Don't mark ourselves suspect
            }
            else
            {
                // Broadcast the suspicion
                memberlist.EncodeAndBroadcast(suspect.Node, Messages.MessageType.Suspect, suspect);
            }

            // Update state
            state.Incarnation = suspect.Incarnation;
            state.State = NodeStateType.Suspect;
            var changeTime = DateTimeOffset.UtcNow;
            state.StateChange = changeTime;

            // Set up suspicion timer
            // k = expected confirmations (suspicionMult - 2, to account for timing)
            var k = memberlist.Config.SuspicionMult - 2;
            var n = memberlist.EstNumNodes();

            // If not enough nodes for confirmations, set k=0
            if (n - 2 < k)
            {
                k = 0;
            }

            // Compute timeouts
            var min = MemberlistMath.SuspicionTimeout(memberlist.Config.SuspicionMult, n, memberlist.Config.ProbeInterval);
            var max = TimeSpan.FromTicks(min.Ticks * memberlist.Config.SuspicionMaxTimeoutMult);

            // Timeout function - marks node as dead
            void TimeoutFn(int numConfirmations)
            {
                Dead? deadMsg = null;

                lock (memberlist.NodeLock)
                {
                    if (memberlist.NodeMap.TryGetValue(suspect.Node, out var currentState))
                    {
                        // Only mark the dead if still suspect and the state hasn't changed
                        var shouldMarkDead = currentState.State == NodeStateType.Suspect &&
                                             currentState.StateChange == changeTime;

                        if (shouldMarkDead)
                        {
                            deadMsg = new Dead
                            {
                                Incarnation = currentState.Incarnation,
                                Node = currentState.Node.Name,
                                From = memberlist.Config.Name
                            };
                        }
                    }
                }

                if (deadMsg == null) return;
                if (k > 0 && numConfirmations < k)
                {
                    // Log degraded state - metrics can be added via delegate if needed
                    logger?.LogDebug("Suspect timeout reached with fewer confirmations than expected: {Actual} < {Expected}",
                        numConfirmations, k);
                }

                logger?.LogInformation("Marking {Node} as failed, suspect timeout reached ({Confirmations} peer confirmations)",
                    state.Node.Name, numConfirmations);

                HandleDeadNode(deadMsg);
            }

            // Create and store suspicion timer
            var newSuspicion = new Suspicion(suspect.From, k, min, max, TimeoutFn);
            memberlist.NodeTimers[suspect.Node] = newSuspicion;
        }
    }

    /// <summary>
    /// Invoked when we get a message about a dead node.
    /// Corresponds to deadNode() in state.go (lines 1253-1310).
    /// </summary>
    public void HandleDeadNode(Dead dead)
    {
        logger?.LogDebug("[HandleDeadNode] Received: Node={Node}, From={From}, Inc={Inc}",
            dead.Node, dead.From, dead.Incarnation);

        lock (memberlist.NodeLock)
        {
            if (!memberlist.NodeMap.TryGetValue(dead.Node, out var state))
            {
                // Never heard of this node, ignore
                logger?.LogDebug("[HandleDeadNode] Unknown node {Node}, ignoring", dead.Node);
                return;
            }

            // Ignore old incarnation
            if (dead.Incarnation < state.Incarnation)
            {
                logger?.LogDebug("[HandleDeadNode] Old incarnation {DeadInc} < {StateInc} for {Node}, ignoring",
                    dead.Incarnation, state.Incarnation, dead.Node);
                return;
            }

            // Clear any suspicion timer
            if (memberlist.NodeTimers.TryRemove(dead.Node, out var timerObj))
            {
                if (timerObj is Suspicion suspicion)
                {
                    try
                    {
                        suspicion.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Timer already disposed, safe to ignore
                    }
                }
            }

            switch (state.State)
            {
                // Check if already in final state
                // CRITICAL: Allow graceful leave (Node==From) to override Dead state
                // This handles the case where the failure detector marks node Dead before graceful leave arrives
                case NodeStateType.Left:
                // Already dead from failure, and this is not a graceful leave override
                case NodeStateType.Dead when dead.Node != dead.From:
                    // Already left, nothing to do
                    return;
            }

            // Check if this is us
            if (state.Node.Name == memberlist.Config.Name)
            {
                // If this is NOT a graceful leave (Node != From) and we're not leaving, refute it
                if (dead.Node != dead.From && !memberlist.IsLeaving)
                {
                    RefuteNode(state, dead.Incarnation);
                    logger?.LogWarning("Refuting dead message from {From}", dead.From);
                    return; // Don't mark ourselves dead
                }

                // If it's a graceful leave (Node==From) or we're leaving, broadcast and continue processing
            }

            // Broadcast the dead message for other nodes
            memberlist.EncodeAndBroadcast(dead.Node, Messages.MessageType.Dead, dead);

            // Update state
            state.Incarnation = dead.Incarnation;

            // If the dead message was sent by the node itself, mark as left instead
            if (dead.Node == dead.From)
            {
                var wasAlreadyDead = state.State == NodeStateType.Dead;
                logger?.LogInformation(
                    wasAlreadyDead
                        ? "[HandleDeadNode] Graceful leave OVERRIDING failure: {Node} Dead→Left"
                        : "[HandleDeadNode] Graceful leave detected (Node==From): {Node} → Left", dead.Node);
                state.State = NodeStateType.Left;
                state.Node.State = NodeStateType.Left; // CRITICAL: Update Node.State too
            }
            else
            {
                logger?.LogInformation("[HandleDeadNode] Node failure detected (Node!=From): {Node} → Dead (reported by {From})",
                    dead.Node, dead.From);
                state.State = NodeStateType.Dead;
                state.Node.State = NodeStateType.Dead; // CRITICAL: Update Node.State too
            }
            state.StateChange = DateTimeOffset.UtcNow;

            // Note: _numNodes is NOT decremented here. Dead/left nodes remain in the node map
            // until they are garbage collected (based on DeadNodeReclaimTime configuration).
            // The count only decrements when nodes are actually removed from _nodeMap.

            // Notify event delegate
            memberlist.Config.Events?.NotifyLeave(state.Node);
        }
    }

    /// <summary>
    /// Refutes incoming information that we are suspect or dead.
    /// Corresponds to refute() in state.go (lines 915-939).
    /// </summary>
    public void RefuteNode(NodeState nodeState, uint accusedIncarnation)
    {
        // Make sure the incarnation number beats the accusation
        var inc = memberlist.NextIncarnation();
        if (accusedIncarnation >= inc)
        {
            inc = memberlist.SkipIncarnation(accusedIncarnation - inc + 1);
        }
        nodeState.Incarnation = inc;

        // Decrease health (being accused is bad for our health)
        memberlist.Awareness.ApplyDelta(1);

        // Format and broadcast alive message
        var alive = new Alive
        {
            Incarnation = inc,
            Node = nodeState.Node.Name,
            Addr = nodeState.Node.Addr.GetAddressBytes(),
            Port = nodeState.Node.Port,
            Meta = nodeState.Node.Meta,
            Vsn =
            [
                nodeState.Node.PMin, nodeState.Node.PMax, nodeState.Node.PCur,
                nodeState.Node.DMin, nodeState.Node.DMax, nodeState.Node.DCur
            ]
        };

        // Broadcast the refutation
        memberlist.EncodeAndBroadcast(nodeState.Node.Name, Messages.MessageType.Alive, alive);

        logger?.LogWarning("Refuted accusation for '{Node}' with incarnation {Inc}", nodeState.Node.Name, inc);
    }

    /// <summary>
    /// Merges remote state from push/pull exchange.
    /// Corresponds to mergeState() in state.go (lines 1314-1340).
    /// </summary>
    public void MergeRemoteState(List<PushNodeState> remoteNodes)
    {
        // This will bump our incarnation and broadcast an Alive message to break the tombstone.
        var ourState = remoteNodes.FirstOrDefault(n => n.Name == memberlist.Config.Name);

        if (
            ourState is { State: NodeStateType.Dead or NodeStateType.Left } &&
            memberlist.NodeMap.TryGetValue(memberlist.Config.Name, out var localState) &&
            ourState.Incarnation >= localState.Incarnation
            )
        {
            logger?.LogWarning("Detected stuck state: Remote has us as {State} with Inc={RemoteInc}, our Inc={OurInc}. Refuting now",
                ourState.State, ourState.Incarnation, localState.Incarnation);

            // Proper refutation: bump incarnation and broadcast Alive
            RefuteNode(localState, ourState.Incarnation);
        }

        foreach (var remote in remoteNodes)
        {
            switch (remote.State)
            {
                case NodeStateType.Alive:
                    var alive = new Alive
                    {
                        Incarnation = remote.Incarnation,
                        Node = remote.Name,
                        Addr = remote.Addr,
                        Port = remote.Port,
                        Meta = remote.Meta,
                        Vsn = remote.Vsn
                    };
                    HandleAliveNode(alive, false);
                    break;

                case NodeStateType.Left:
                    // Left nodes send a dead message from themselves
                    var left = new Dead
                    {
                        Incarnation = remote.Incarnation,
                        Node = remote.Name,
                        From = remote.Name
                    };
                    HandleDeadNode(left);
                    break;

                case NodeStateType.Dead:
                    // Prefer to suspect rather than immediately mark dead
                    goto case NodeStateType.Suspect;

                case NodeStateType.Suspect:
                    var suspect = new Suspect
                    {
                        Incarnation = remote.Incarnation,
                        Node = remote.Name,
                        From = memberlist.Config.Name
                    };
                    HandleSuspectNode(suspect);
                    break;
            }
        }
    }
}
