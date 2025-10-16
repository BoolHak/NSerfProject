// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/coalesce.go

using System.Threading.Channels;
using NSerf.Serf.Events;

namespace NSerf.Serf.Coalesce;

/// <summary>
/// Coalescing utilities for event channels.
/// </summary>
internal static class CoalesceLoop
{
    /// <summary>
    /// CoalescedEventCh returns an event channel where the events are coalesced using the given coalescer.
    /// </summary>
    /// <param name="outCh">Output channel for coalesced events</param>
    /// <param name="shutdownToken">Cancellation token for shutdown</param>
    /// <param name="coalescePeriod">Maximum time to wait before flushing (quantum period)</param>
    /// <param name="quiescentPeriod">Time to wait for quiescence before flushing</param>
    /// <param name="coalescer">The coalescer implementation to use</param>
    /// <returns>Input channel for receiving events</returns>
    public static ChannelWriter<Event> CoalescedEventChannel(
        ChannelWriter<Event> outCh,
        CancellationToken shutdownToken,
        TimeSpan coalescePeriod,
        TimeSpan quiescentPeriod,
        ICoalescer coalescer)
    {
        var channel = Channel.CreateUnbounded<Event>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        });

        _ = Task.Run(async () =>
        {
            await RunCoalesceLoopAsync(
                channel.Reader,
                outCh,
                shutdownToken,
                coalescePeriod,
                quiescentPeriod,
                coalescer);
        }, shutdownToken);

        return channel.Writer;
    }

    /// <summary>
    /// CoalesceLoop is a simple long-running routine that manages the high-level
    /// flow of coalescing based on quiescence and a maximum quantum period.
    /// Matches Go's select-based implementation.
    /// </summary>
    private static async Task RunCoalesceLoopAsync(
        ChannelReader<Event> inCh,
        ChannelWriter<Event> outCh,
        CancellationToken shutdownToken,
        TimeSpan coalescePeriod,
        TimeSpan quiescentPeriod,
        ICoalescer coalescer)
    {
        var shutdown = false;

        while (!shutdown)
        {
            // INGEST: Reset the timers for this cycle
            CancellationTokenSource? quantumCts = null;
            CancellationTokenSource? quiescentCts = null;

            try
            {
                // Ingest loop - matches Go's select statement
                while (true)
                {
                    // Build the list of tasks to wait for (simulates Go's select)
                    var waitTasks = new List<Task>();
                    var eventTask = inCh.WaitToReadAsync(shutdownToken).AsTask();
                    waitTasks.Add(eventTask);

                    Task? quantumTask = null;
                    if (quantumCts != null && !quantumCts.Token.IsCancellationRequested)
                    {
                        quantumTask = Task.Delay(Timeout.Infinite, quantumCts.Token);
                        waitTasks.Add(quantumTask);
                    }

                    Task? quiescentTask = null;
                    if (quiescentCts != null && !quiescentCts.Token.IsCancellationRequested)
                    {
                        quiescentTask = Task.Delay(Timeout.Infinite, quiescentCts.Token);
                        waitTasks.Add(quiescentTask);
                    }

                    var shutdownTask = Task.Delay(Timeout.Infinite, shutdownToken);
                    waitTasks.Add(shutdownTask);

                    // Wait for first task to complete (simulates select)
                    var completed = await Task.WhenAny(waitTasks);

                    // Check shutdown - but first drain any pending events before flushing
                    if (shutdownToken.IsCancellationRequested)
                    {
                        // Drain any events that are already in the channel before shutdown
                        while (inCh.TryRead(out var pendingEvent))
                        {
                            if (coalescer.Handle(pendingEvent))
                            {
                                coalescer.Coalesce(pendingEvent);
                            }
                            else
                            {
                                // Pass through non-coalesceable events
                                try { await outCh.WriteAsync(pendingEvent); } catch { }
                            }
                        }
                        
                        shutdown = true;
                        break; // Will flush in FLUSH section below
                    }

                    // Check if quantum timer was cancelled (fired)
                    if (quantumTask != null && completed == quantumTask)
                    {
                        break; // FLUSH
                    }

                    // Check if quiescent timer was cancelled (fired)
                    if (quiescentTask != null && completed == quiescentTask)
                    {
                        break; // FLUSH
                    }

                    // Must be event available
                    if (completed == eventTask && !shutdownToken.IsCancellationRequested)
                    {
                        if (inCh.TryRead(out var e))
                        {
                            // Check if coalescer handles this event
                            if (!coalescer.Handle(e))
                            {
                                // Pass through immediately
                                await outCh.WriteAsync(e, shutdownToken);
                                continue;
                            }

                            // Start quantum timer if first event
                            if (quantumCts == null)
                            {
                                quantumCts = new CancellationTokenSource();
                                quantumCts.CancelAfter(coalescePeriod);
                            }

                            // Always restart quiescent timer
                            quiescentCts?.Dispose();
                            quiescentCts = new CancellationTokenSource();
                            quiescentCts.CancelAfter(quiescentPeriod);

                            // Coalesce the event
                            coalescer.Coalesce(e);
                        }
                    }
                }

                // FLUSH: Flush the coalesced events (including on shutdown)
                try
                {
                    coalescer.Flush(outCh);
                }
                catch (ChannelClosedException)
                {
                    // Channel may be closed during shutdown, which is acceptable
                }
            }
            finally
            {
                // Clean up cancellation tokens
                quantumCts?.Dispose();
                quiescentCts?.Dispose();
            }
        }
    }

    private enum FlushReason
    {
        None,
        Quantum,
        Quiescent,
        Shutdown
    }
}
