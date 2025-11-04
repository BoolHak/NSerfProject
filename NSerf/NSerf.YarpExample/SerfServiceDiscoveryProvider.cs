using Microsoft.Extensions.Primitives;
using NSerf.Agent;
using NSerf.Serf.Events;
using System.Net;
using System.Net.Sockets;
using Yarp.ReverseProxy.Configuration;

namespace NSerf.YarpExample;

/// <summary>
/// Dynamic service discovery provider that integrates NSerf cluster membership with YARP.
/// Uses Serf's event system for immediate updates when members join/leave/fail,
/// with a slower reconciliation timer as fallback for eventual consistency.
/// </summary>
public class SerfServiceDiscoveryProvider : IProxyConfigProvider, IEventHandler, IDisposable
{
    private readonly SerfAgent _agent;
    private readonly ILogger<SerfServiceDiscoveryProvider> _logger;
    private readonly Timer _reconciliationTimer;
    private volatile InMemoryConfig _config;

    private int _updating = 0;
    private bool _hadBackends = false;
    private bool _disposed = false;

    public SerfServiceDiscoveryProvider(
        SerfAgent agent,
        ILogger<SerfServiceDiscoveryProvider> logger)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = new InMemoryConfig([], []);

        // Register event handler for immediate updates
        _agent.RegisterEventHandler(this);

        // Slower reconciliation timer as fallback (every 30 seconds)
        // This ensures eventual consistency even if events are missed
        _reconciliationTimer = new Timer(UpdateConfiguration, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

        _logger.LogInformation("SerfServiceDiscoveryProvider initialized with event-driven updates");
    }

    public IProxyConfig GetConfig() => _config;

    /// <summary>
    /// Handle Serf events for immediate configuration updates.
    /// </summary>
    public void HandleEvent(IEvent @event)
    {
        if (_disposed)
            return;

        // Only care about member events that affect backend availability
        if (@event is not MemberEvent memberEvent) return;
        var eventType = memberEvent.Type;

        // Log event for visibility
        var memberNames = string.Join(", ", memberEvent.Members.Select(m => m.Name));
        _logger.LogDebug("Received {EventType} for members: {Members}", eventType.String(), memberNames);

        // Check if any affected members are backends
        var hasBackendMembers = memberEvent.Members.Any(m =>
            m.Tags.TryGetValue("service", out var svc) && svc == "backend");

        if (!hasBackendMembers) return;
        _logger.LogInformation("Backend member {EventType} detected - updating configuration", eventType.String());
        UpdateConfiguration(null);
    }
    private static string BuildAddress(IPAddress ip, string port, string scheme = "http")
    {
        var host = ip.ToString();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            host = host.Replace("%", "%25");
            host = $"[{host}]";
        }
        return $"{scheme}://{host}:{port}";
    }

    private void UpdateConfiguration(object? state)
    {
        if (Interlocked.Exchange(ref _updating, 1) == 1)
            return;

        try
        {
            if (_agent.Serf == null)
            {
                _logger.LogWarning("Serf instance not available yet");
                return;
            }

            var members = _agent.Serf.Members()
                .Where(m => m.Status == Serf.MemberStatus.Alive)
                .Where(m => m.Tags.TryGetValue("service", out var svc) && svc == "backend")
                .ToList();

            var newDestInfos = members.Select(m =>
            {
                var scheme = m.Tags.GetValueOrDefault("scheme", "http");
                var port = m.Tags.GetValueOrDefault("http-port", "5000");
                var address = BuildAddress(m.Addr, port, scheme);
                // Stable key from identity; fallback to address
                var key = m.Tags.TryGetValue("instance", out var inst) && !string.IsNullOrWhiteSpace(inst)
                            ? inst
                            : $"{m.Addr}:{port}";
                return (key, address);

            }).OrderBy(x => x.key).ToList();

            var currentConfig = _config;
            var currentAddresses = currentConfig.Clusters.FirstOrDefault()?.Destinations?
                .OrderBy(kv => kv.Key)
                .Select(kv => (key: kv.Key, address: kv.Value.Address))
                .ToList() ?? [];

            var changed =
                newDestInfos.Count != currentAddresses.Count
                || newDestInfos.Zip(currentAddresses, (n, c) => n.key != c.key || n.address != c.address)
                               .Any(diff => diff);

            if (newDestInfos.Count == 0)
            {
                if (_hadBackends)
                    _logger.LogWarning("All backend services disappeared from the cluster; clearing YARP destinations.");
                _hadBackends = false;

                if (currentAddresses.Count == 0)
                    return; // already empty, nothing to do

                PushConfig([]);
                return;
            }

            if (!changed)
                return;

            _hadBackends = true;
            _logger.LogInformation("Configuration changed - updating YARP with {Count} backend services", newDestInfos.Count);
            PushConfig(newDestInfos);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating YARP configuration from Serf");
        }
        finally
        {
            Interlocked.Exchange(ref _updating, 0);
        }
    }

    private void PushConfig(IEnumerable<(string key, string addr)> destinationsInfo)
    {
        var valueTuples = destinationsInfo.ToList();
        var destinations = valueTuples
            .ToDictionary(x => x.key, x => new DestinationConfig { Address = x.addr });

        var cluster = new ClusterConfig
        {
            ClusterId = "backend-cluster",
            Destinations = destinations,
            LoadBalancingPolicy = "RoundRobin",
            HealthCheck = new HealthCheckConfig
            {
                Active = new ActiveHealthCheckConfig
                {
                    Enabled = true,
                    Interval = TimeSpan.FromSeconds(2),
                    Timeout = TimeSpan.FromSeconds(1),
                    Path = "/health"
                }
            }
        };

        var route = new RouteConfig
        {
            RouteId = "backend-route",
            ClusterId = "backend-cluster",
            Match = new RouteMatch { Path = "/{**catch-all}" }
        };

        var newConfig = new InMemoryConfig([route], [cluster]);
        var oldConfig = Interlocked.Exchange(ref _config, newConfig);
        oldConfig?.SignalChange();

        foreach (var (_, addr) in valueTuples)
            _logger.LogInformation("  → Backend: {Address}", addr);

        _ = Task.Delay(1000).ContinueWith(_ => oldConfig?.Dispose());
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Deregister event handler
        _agent.DeregisterEventHandler(this);

        _reconciliationTimer?.Dispose();
        _config?.Dispose();
    }

    private class InMemoryConfig : IProxyConfig, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public InMemoryConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeToken = new CancellationChangeToken(_cts.Token);
        }

        public IReadOnlyList<RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken { get; }

        public void SignalChange()
        {
            _cts.Cancel();
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}
