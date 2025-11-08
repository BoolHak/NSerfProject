using Microsoft.Extensions.Logging;

namespace NSerf.Memberlist;

public partial class Memberlist
{
    private readonly List<Task> _backgroundTasks = [];

    /// <summary>
    /// Starts background listeners for the transport to ingest packets and streams.
    /// </summary>
    private void StartBackgroundListeners()
    {
        SetupPacketTask();
        SetupStreamTask();
        SetupGossipTask();
        if (Config.ProbeInterval > TimeSpan.Zero) SetUpProbeTask();
        if (Config.PushPullInterval > TimeSpan.Zero) SetupPushPullTask();

    }

    private void SetupPacketTask()
    {
        var packetTask = Task.Factory.StartNew(async () =>
        {
            try
            {
                var reader = _transport.PacketChannel;
                while (!_shutdownCts.IsCancellationRequested)
                {
                    try
                    {
                        if (!await reader.WaitToReadAsync(_shutdownCts.Token)) break;
                        while (reader.TryRead(out var p))
                        {
                            _packetHandler.IngestPacket(p.Buf, p.From, p.Timestamp);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Packet listener error");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Packet listener crashed");
            }
        }, _shutdownCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        _backgroundTasks.Add(packetTask);
    }

    private void SetupStreamTask()
    {
        var streamTask = Task.Factory.StartNew(async () =>
        {
            try
            {
                var reader = _transport.StreamChannel;
                while (!_shutdownCts.IsCancellationRequested)
                {
                    try
                    {
                        if (!await reader.WaitToReadAsync(_shutdownCts.Token)) break;
                        while (reader.TryRead(out var stream))
                        {
                            // Handle stream connection in the background
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await HandleStreamAsync(stream);
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogWarning(ex, "Stream handler error");
                                }
                                finally
                                {
                                    await stream.DisposeAsync();
                                }
                            }, _shutdownCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Stream listener error");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Stream listener crashed");
            }
        }, _shutdownCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        _backgroundTasks.Add(streamTask);
    }

    private void SetupGossipTask()
    {

        var gossipTask = Task.Factory.StartNew(async () =>
        {
            try
            {
                while (!_shutdownCts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(Config.GossipInterval, _shutdownCts.Token);
                        await GossipAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Gossip scheduler error");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Gossip scheduler crashed");
            }
        }, _shutdownCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        _backgroundTasks.Add(gossipTask);
    }

    private void SetUpProbeTask()
    {
        var probeTask = Task.Factory.StartNew(async () =>
        {
            try
            {
                // Add initial random stagger to avoid synchronization
                var stagger =
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)Config.ProbeInterval.TotalMilliseconds));
                await Task.Delay(stagger, _shutdownCts.Token);

                while (!_shutdownCts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(Config.ProbeInterval, _shutdownCts.Token);
                        await ProbeAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Probe scheduler error");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Probe scheduler crashed");
            }
        }, _shutdownCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        _backgroundTasks.Add(probeTask);
    }

    private void SetupPushPullTask()
    {
        var pushPullTask = Task.Factory.StartNew(async () =>
        {
            try
            {
                // Add initial random stagger to avoid synchronization
                var stagger =
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)Config.PushPullInterval.TotalMilliseconds));
                await Task.Delay(stagger, _shutdownCts.Token);

                while (!_shutdownCts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(Config.PushPullInterval, _shutdownCts.Token);
                        await PeriodicPushPullAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Push-pull scheduler error");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Push-pull scheduler crashed");
            }
        }, _shutdownCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        _backgroundTasks.Add(pushPullTask);
    }
}