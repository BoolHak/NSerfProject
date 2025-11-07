namespace NSerf.ServiceDiscovery;

/// <summary>
/// Interface for service discovery exporters (data consumers).
/// Implementations can export discovered services to various systems:
/// - YARP reverse proxy
/// - nginx configuration
/// - HAProxy configuration
/// - Envoy xDS API
/// - Custom load balancers
/// - etc.
/// </summary>
public interface IServiceExporter
{
    /// <summary>
    /// Exporter name (e.g., "YARP", "nginx", "Envoy")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Starts the exporter
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the exporter
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports services to the target system
    /// </summary>
    Task ExportAsync(IReadOnlyList<Service> services, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this exporter supports incremental updates
    /// </summary>
    bool SupportsIncrementalUpdates { get; }
}
