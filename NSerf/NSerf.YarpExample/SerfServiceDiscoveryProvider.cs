using Microsoft.Extensions.Primitives;
using NSerf.Agent;
using Yarp.ReverseProxy.Configuration;

namespace NSerf.YarpExample;

/// <summary>
/// Dynamic service discovery provider that integrates NSerf cluster membership with YARP.
/// Automatically discovers backend services from the Serf cluster and updates YARP routing.
/// </summary>
public class SerfServiceDiscoveryProvider : IProxyConfigProvider, IDisposable
{
    private readonly SerfAgent _agent;
    private readonly ILogger<SerfServiceDiscoveryProvider> _logger;
    private readonly Timer _updateTimer;
    private volatile InMemoryConfig _config;

    public SerfServiceDiscoveryProvider(
        SerfAgent agent,
        ILogger<SerfServiceDiscoveryProvider> logger)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize with empty configuration
        _config = new InMemoryConfig(Array.Empty<RouteConfig>(), Array.Empty<ClusterConfig>());

        // Poll for changes every 5 seconds
        _updateTimer = new Timer(UpdateConfiguration, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

        _logger.LogInformation("SerfServiceDiscoveryProvider initialized");
    }

    public IProxyConfig GetConfig() => _config;

    private void UpdateConfiguration(object? state)
    {
        try
        {
            if (_agent.Serf == null)
            {
                _logger.LogWarning("Serf instance not available yet");
                return;
            }

            var members = _agent.Serf.Members()
                .Where(m => m.Status == NSerf.Serf.MemberStatus.Alive)
                .Where(m => m.Tags.ContainsKey("service") && m.Tags["service"] == "backend")
                .ToList();

            if (members.Count == 0)
            {
                _logger.LogWarning("No backend services discovered in cluster");
                return;
            }

            // Build destination addresses for comparison
            var newAddresses = members
                .Select(m => $"http://{m.Addr}:{m.Tags.GetValueOrDefault("http-port", "5000")}")
                .OrderBy(a => a)
                .ToList();

            // Check if configuration actually changed
            var currentAddresses = _config.Clusters.FirstOrDefault()?.Destinations
                ?.Select(d => d.Value.Address)
                .OrderBy(a => a)
                .ToList() ?? new List<string>();

            if (newAddresses.SequenceEqual(currentAddresses))
            {
                // Configuration unchanged, skip update
                return;
            }

            _logger.LogInformation("Configuration changed - updating YARP with {Count} backend services", members.Count);

            // Create destinations from Serf members
            var destinations = newAddresses
                .Select(addr => new DestinationConfig { Address = addr })
                .ToDictionary(d => Guid.NewGuid().ToString(), d => d);

            // Create cluster configuration
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
                        Interval = TimeSpan.FromSeconds(10),
                        Timeout = TimeSpan.FromSeconds(5),
                        Policy = "ConsecutiveFailures",
                        Path = "/health"
                    }
                }
            };

            // Create route configuration
            var route = new RouteConfig
            {
                RouteId = "backend-route",
                ClusterId = "backend-cluster",
                Match = new RouteMatch
                {
                    Path = "/{**catch-all}"
                }
            };

            // Create new configuration with fresh token
            var newConfig = new InMemoryConfig(new[] { route }, new[] { cluster });
            
            // Atomically swap configurations
            var oldConfig = Interlocked.Exchange(ref _config, newConfig);
            
            // Signal change AFTER swap (so YARP gets new config on reload)
            oldConfig?.SignalChange();
            
            // Log the changes
            foreach (var addr in newAddresses)
            {
                _logger.LogInformation("  → Backend: {Address}", addr);
            }
            
            // Dispose old config after a delay to ensure YARP has finished reload
            _ = Task.Delay(1000).ContinueWith(_ => oldConfig?.Dispose());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating YARP configuration from Serf");
        }
    }

    public void Dispose()
    {
        _updateTimer?.Dispose();
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
