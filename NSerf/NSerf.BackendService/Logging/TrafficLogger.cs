using Microsoft.Extensions.Logging;
using NSerf.BackendService.Services;
using System.Text.RegularExpressions;

namespace NSerf.BackendService.Logging;

public class TrafficLoggerProvider : ILoggerProvider
{
    private readonly NetworkTrafficMonitor _monitor;

    public TrafficLoggerProvider(NetworkTrafficMonitor monitor)
    {
        _monitor = monitor;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TrafficLogger(categoryName, _monitor);
    }

    public void Dispose() { }
}

public class TrafficLogger : ILogger
{
    private readonly string _categoryName;
    private readonly NetworkTrafficMonitor _monitor;

    // Regex patterns to capture node names from logs
    // Gossip to specific node - "[GOSSIP] Gossiping to backend-2"
    private static readonly Regex GossipRegex = new(@"\[GOSSIP\] Gossiping to (.+)", RegexOptions.Compiled);
    private static readonly Regex ProbeRegex = new(@"Probing node: (.+)", RegexOptions.Compiled);
    private static readonly Regex PushPullRegex = new(@"\[PUSH-PULL\] Starting periodic push-pull with (.+)", RegexOptions.Compiled);
    // For Ping packets - "[PACKET] Received 32 bytes from 172.19.0.3:7948"
    private static readonly Regex PacketRegex = new(@"\[PACKET\] Received (\d+) bytes from (.+)", RegexOptions.Compiled);

    public TrafficLogger(string categoryName, NetworkTrafficMonitor monitor)
    {
        _categoryName = categoryName;
        _monitor = monitor;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // We accept Memberlist logs and SerfAgent logs (since we reused the logger)
        if (!_categoryName.StartsWith("NSerf")) return;

        var message = formatter(state, exception);
        Console.WriteLine($"[TrafficLogger] Received: {message}"); // DEBUG

        if (string.IsNullOrEmpty(message)) return;

        // Check patterns
        // 1. Gossip to specific node - now includes target node name!
        var gossipMatch = GossipRegex.Match(message);
        if (gossipMatch.Success)
        {
            var targetNode = gossipMatch.Groups[1].Value; // e.g., "backend-2"
            _monitor.Report("gossip", targetNode, 0);
            return;
        }

        // 2. Probe - specific node
        var probeMatch = ProbeRegex.Match(message);
        if (probeMatch.Success)
        {
            _monitor.Report("probe", probeMatch.Groups[1].Value, 0);
            return;
        }

        // 3. PushPull - specific node
        var pushPullMatch = PushPullRegex.Match(message);
        if (pushPullMatch.Success)
        {
            _monitor.Report("pushpull", pushPullMatch.Groups[1].Value, 0);
            return;
        }
        
        // 4. Packet received - shows incoming traffic from specific address
        var packetMatch = PacketRegex.Match(message);
        if (packetMatch.Success)
        {
            var addressWithPort = packetMatch.Groups[2].Value; // e.g., "172.19.0.3:7948"
            _monitor.Report("packet", addressWithPort, int.Parse(packetMatch.Groups[1].Value));
            return;
        }
    }
}
