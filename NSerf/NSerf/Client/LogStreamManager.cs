using Microsoft.Extensions.Logging;

namespace NSerf.Client;

/// <summary>
/// Manages log streaming from Serf's ILogger to multiple IPC monitor clients.
/// Captures logs and multicasts them to registered LogStream instances.
/// Phase 16 - Task 1.2 (TDD Implementation).
/// </summary>
internal class LogStreamManager
{
    private readonly ILogger _serfLogger;
    private readonly Dictionary<ulong, LogStreamRegistration> _activeMonitors = new();
    private readonly object _lock = new();

    public LogStreamManager(ILogger serfLogger)
    {
        _serfLogger = serfLogger ?? throw new ArgumentNullException(nameof(serfLogger));
        
        // Hook into the logger to capture logs (for testing)
        // In production, this will use ILoggerProvider pattern
        if (serfLogger is ILoggableLogger loggableLogger)
        {
            loggableLogger.AddLogHandler(OnLogReceived);
        }
    }

    /// <summary>
    /// Registers a new log monitor for a client.
    /// </summary>
    public void RegisterMonitor(ulong seq, object client, string logLevel, List<string> receivedLogs, CancellationToken ct)
    {
        var minLevel = ParseLogLevel(logLevel);
        var logStream = new LogStream(client, seq, minLevel, ct);
        
        lock (_lock)
        {
            _activeMonitors[seq] = new LogStreamRegistration
            {
                Seq = seq,
                MinLevel = minLevel,
                ReceivedLogs = receivedLogs,
                CancellationToken = ct,
                Stream = logStream
            };
        }
    }

    /// <summary>
    /// Unregisters a log monitor.
    /// </summary>
    public void UnregisterMonitor(ulong seq)
    {
        lock (_lock)
        {
            _activeMonitors.Remove(seq);
        }
    }

    /// <summary>
    /// Called when a log is received from the wrapped logger.
    /// </summary>
    private void OnLogReceived(LogLevel level, string message)
    {
        // Fan-out to all registered monitors
        List<LogStreamRegistration> monitors;
        lock (_lock)
        {
            monitors = _activeMonitors.Values.ToList();
        }

        foreach (var monitor in monitors)
        {
            if (level >= monitor.MinLevel)
            {
                // Add to received logs list (for testing)
                monitor.ReceivedLogs.Add(message);
                
                // Phase 16: Actually send to client
                if (monitor.Stream != null)
                {
                    try
                    {
                        _ = monitor.Stream.SendLogAsync(message, level);
                    }
                    catch
                    {
                        // Client disconnected, will be cleaned up later
                    }
                }
            }
        }
    }

    private LogLevel ParseLogLevel(string logLevel)
    {
        return logLevel.ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "info" => LogLevel.Information,
            "warn" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => LogLevel.Debug
        };
    }

    private class LogStreamRegistration
    {
        public ulong Seq { get; set; }
        public LogLevel MinLevel { get; set; }
        public List<string> ReceivedLogs { get; set; } = new();
        public CancellationToken CancellationToken { get; set; }
        public LogStream? Stream { get; set; }
    }
}

/// <summary>
/// Interface for loggers that support hooking into log output.
/// Used by LogStreamManager to capture logs for streaming.
/// </summary>
internal interface ILoggableLogger
{
    void AddLogHandler(Action<LogLevel, string> handler);
}
