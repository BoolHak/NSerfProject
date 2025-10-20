// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Phase 9.4: Background Tasks (Reaper and Reconnect)

using Microsoft.Extensions.Logging;
using NSerf.Serf.Events;

namespace NSerf.Serf;

/// <summary>
/// Background tasks for Serf - Reaper and Reconnect functionality
/// </summary>
public partial class Serf
{
    // Background task cancellation tokens
    private Task? _reapTask;
    private Task? _reconnectTask;
    private Task? _queueMonitorTask;

    /// <summary>
    /// Starts the background tasks (reaper, reconnect, and queue monitoring).
    /// Called from CreateAsync after Serf is fully initialized.
    /// </summary>
    private void StartBackgroundTasks()
    {
        _reapTask = Task.Run(HandleReapAsync);
        _reconnectTask = Task.Run(HandleReconnectAsync);
        _queueMonitorTask = Task.Run(HandleQueueMonitorAsync);
    }

    /// <summary>
    /// Periodically reaps the list of failed and left members.
    /// Runs on ReapInterval until shutdown.
    /// </summary>
    private async Task HandleReapAsync()
    {
        try
        {
            while (!_shutdownCts.Token.IsCancellationRequested)
            {
                await Task.Delay(Config.ReapInterval, _shutdownCts.Token);

                WithWriteLock(_memberLock, () =>
                {
                    var now = DateTimeOffset.UtcNow;
                    Reap(FailedMembers, now, Config.ReconnectTimeout);
                    Reap(LeftMembers, now, Config.TombstoneTimeout);
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[Serf] HandleReap error");
        }
    }

    /// <summary>
    /// Periodically attempts to reconnect to recently failed nodes.
    /// Runs on ReconnectInterval until shutdown.
    /// </summary>
    private async Task HandleReconnectAsync()
    {
        try
        {
            while (!_shutdownCts.Token.IsCancellationRequested)
            {
                await Task.Delay(Config.ReconnectInterval, _shutdownCts.Token);
                await ReconnectAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[Serf] HandleReconnect error");
        }
    }

    /// <summary>
    /// Periodically monitors queue depths and emits metrics.
    /// Runs on QueueCheckInterval until shutdown.
    /// Reference: Go serf.go:1690-1696
    /// </summary>
    private async Task HandleQueueMonitorAsync()
    {
        try
        {
            while (!_shutdownCts.Token.IsCancellationRequested)
            {
                await Task.Delay(Config.QueueCheckInterval, _shutdownCts.Token);

                // Check event queue depth
                var eventQueueDepth = EventBroadcasts.Count;
                Config.Metrics.AddSample(new[] { "serf", "queue", "event" }, eventQueueDepth, Config.MetricLabels);
                
                if (eventQueueDepth >= Config.QueueDepthWarning)
                {
                    Logger?.LogWarning("[Serf] event queue depth: {Depth}", eventQueueDepth);
                }

                // Check query queue depth
                var queryQueueDepth = QueryBroadcasts.Count;
                Config.Metrics.AddSample(new[] { "serf", "queue", "query" }, queryQueueDepth, Config.MetricLabels);
                
                if (queryQueueDepth >= Config.QueueDepthWarning)
                {
                    Logger?.LogWarning("[Serf] query queue depth: {Depth}", queryQueueDepth);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[Serf] HandleQueueMonitor error");
        }
    }

    /// <summary>
    /// Reaps (removes) old members from a list that have exceeded the timeout.
    /// Modifies the list in-place using reverse iteration for efficient removal.
    /// </summary>
    /// <param name="members">List of members to check and modify</param>
    /// <param name="now">Current time</param>
    /// <param name="timeout">Timeout duration</param>
    /// <remarks>
    /// Uses reverse iteration to avoid index shifting issues when removing items.
    /// This approach reduces GC pressure compared to creating a new list.
    /// </remarks>
    private void Reap(List<MemberInfo> members, DateTimeOffset now, TimeSpan timeout)
    {
        // Iterate in reverse to safely remove items
        for (int i = members.Count - 1; i >= 0; i--)
        {
            var member = members[i];
            var memberTimeout = timeout;

            // Check if we should override the timeout (for dynamic timeout per member)
            if (Config.ReconnectTimeoutOverride != null)
            {
                memberTimeout = Config.ReconnectTimeoutOverride.ReconnectTimeout(member.Member, memberTimeout);
            }

            // Skip if timeout not yet reached
            if (now - member.LeaveTime <= memberTimeout)
            {
                continue;
            }

            // Timeout exceeded - erase this member and remove from list
            Logger?.LogInformation("[Serf] EventMemberReap: {Name}", member.Name);
            EraseNode(member);
            members.RemoveAt(i);
        }
    }

    /// <summary>
    /// Completely removes a node from the member list and emits EventMemberReap.
    /// </summary>
    /// <param name="member">Member to erase</param>
    /// <remarks>
    /// THREAD SAFETY: This method assumes _memberLock write lock is already held by the caller.
    /// It accesses MemberStates which requires synchronization.
    /// </remarks>
    private void EraseNode(MemberInfo member)
    {
        // Delete from members map (using helper to synchronize both structures)
        RemoveMemberState(member.Name);

        // Coordinate client cleanup (matches Go eraseNode)
        if (!Config.DisableCoordinates)
        {
            try
            {
                _coordClient?.ForgetNode(member.Name);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "[Serf] CoordClient.ForgetNode error for {Name}", member.Name);
            }

            _coordCacheLock.EnterWriteLock();
            try
            {
                // Remove cached coordinate entry for this node
                if (_coordCache != null)
                {
                    _coordCache.Remove(member.Name);
                }
            }
            finally
            {
                _coordCacheLock.ExitWriteLock();
            }
        }

        // Emit EventMemberReap
        if (Config.EventCh != null)
        {
            var reapEvent = new MemberEvent
            {
                Type = EventType.MemberReap,
                Members = new List<Member> { member.Member }
            };

            Config.EventCh.TryWrite(reapEvent);
        }
    }

    /// <summary>
    /// Attempts to reconnect to a randomly selected failed node.
    /// Uses probabilistic selection based on failed vs alive member ratio.
    /// </summary>
    private async Task ReconnectAsync()
    {
        int numFailed = 0;
        int numAlive = 0;
        MemberInfo? selectedMember = null;

        WithReadLock(_memberLock, () =>
        {
            numFailed = FailedMembers.Count;
            if (numFailed == 0)
            {
                return; // Nothing to do
            }

            // Calculate probability of attempting reconnect
            // prob = numFailed / (total - numFailed - numLeft)
            numAlive = MemberStates.Count - FailedMembers.Count - LeftMembers.Count;
            if (numAlive == 0)
            {
                numAlive = 1; // Guard against divide by zero
            }

            var prob = (float)numFailed / numAlive;
            if (Random.Shared.NextSingle() > prob)
            {
                Logger?.LogDebug("[Serf] Forgoing reconnect for random throttling");
                return;
            }

            // Select a random failed member
            var idx = Random.Shared.Next(numFailed);
            selectedMember = FailedMembers[idx];
        });

        if (selectedMember == null)
        {
            return;
        }

        // Attempt to reconnect
        var addr = $"{selectedMember.Member.Addr}:{selectedMember.Member.Port}";
        Logger?.LogInformation("[Serf] Attempting reconnect to {Name} {Addr}", selectedMember.Name, addr);

        var joinAddr = string.IsNullOrEmpty(selectedMember.Name)
            ? addr
            : $"{selectedMember.Name}/{addr}";

        try
        {
            // Attempt to join at memberlist level
            if (Memberlist != null)
            {
                await Memberlist.JoinAsync(new[] { joinAddr }, _shutdownCts.Token);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "[Serf] Reconnect to {Name} failed", selectedMember.Name);
        }
    }
}
