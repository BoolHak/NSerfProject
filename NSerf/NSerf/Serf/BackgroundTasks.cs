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

    /// <summary>
    /// Starts the background tasks (reaper and reconnect).
    /// Called from CreateAsync after Serf is fully initialized.
    /// </summary>
    private void StartBackgroundTasks()
    {
        _reapTask = Task.Run(async () => await HandleReapAsync());
        _reconnectTask = Task.Run(async () => await HandleReconnectAsync());
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
                    FailedMembers = Reap(FailedMembers, now, Config.ReconnectTimeout);
                    LeftMembers = Reap(LeftMembers, now, Config.TombstoneTimeout);
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
    /// Reaps (removes) old members from a list that have exceeded the timeout.
    /// Returns the updated list with expired members removed.
    /// </summary>
    /// <param name="oldMembers">List of members to check</param>
    /// <param name="now">Current time</param>
    /// <param name="timeout">Timeout duration</param>
    /// <returns>Updated list with expired members removed</returns>
    private List<MemberInfo> Reap(List<MemberInfo> oldMembers, DateTimeOffset now, TimeSpan timeout)
    {
        var remaining = new List<MemberInfo>();

        foreach (var member in oldMembers)
        {
            var memberTimeout = timeout;

            // Check if we should override the timeout (for dynamic timeout per member)
            if (Config.ReconnectTimeoutOverride != null)
            {
                memberTimeout = Config.ReconnectTimeoutOverride.ReconnectTimeout(member.Member, memberTimeout);
            }

            // Skip if timeout not yet reached
            if (now - member.LeaveTime <= memberTimeout)
            {
                remaining.Add(member);
                continue;
            }

            // Timeout exceeded - erase this member
            Logger?.LogInformation("[Serf] EventMemberReap: {Name}", member.Name);
            EraseNode(member);
        }

        return remaining;
    }

    /// <summary>
    /// Completely removes a node from the member list and emits EventMemberReap.
    /// </summary>
    /// <param name="member">Member to erase</param>
    private void EraseNode(MemberInfo member)
    {
        // Delete from members map
        MemberStates.Remove(member.Name);

        // TODO: Phase 10+ - Coordinate client cleanup
        // if (!Config.DisableCoordinates)
        // {
        //     CoordClient.ForgetNode(member.Name);
        //     CoordCache.Remove(member.Name);
        // }

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
                await Memberlist.JoinAsync(new[] { joinAddr }, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "[Serf] Reconnect to {Name} failed", selectedMember.Name);
        }
    }
}
