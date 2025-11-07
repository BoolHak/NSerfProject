namespace NSerf.ServiceDiscovery;

/// <summary>
/// Interface for service discovery providers (data sources).
/// Implementations can pull service information from various sources:
/// - Serf membership
/// - Consul
/// - etcd
/// - Kubernetes
/// - Static configuration
/// - etc.
/// </summary>
public interface IServiceProvider
{
    /// <summary>
    /// Provider name (e.g., "Serf", "Consul", "Kubernetes")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Starts the provider and begins monitoring for service changes
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the provider
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all current services from the provider
    /// </summary>
    Task<IReadOnlyList<Service>> DiscoverServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when the provider detects service changes
    /// </summary>
    event EventHandler<ServiceChangedEventArgs>? ServiceDiscovered;
}
