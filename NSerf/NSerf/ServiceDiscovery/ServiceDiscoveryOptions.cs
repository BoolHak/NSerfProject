namespace NSerf.ServiceDiscovery;

/// <summary>
/// Configuration options for the generic service discovery system
/// </summary>
public sealed class ServiceDiscoveryOptions
{
    /// <summary>
    /// How often to reconcile/sync services from providers (fallback timer)
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan ReconcileInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to enable automatic health checking of discovered instances
    /// Default: true
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>
    /// Health check interval for active probes
    /// Default: 10 seconds
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Health check timeout
    /// Default: 5 seconds
    /// </summary>
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Number of consecutive health check failures before marking instance as unhealthy
    /// Default: 3
    /// </summary>
    public int HealthCheckFailureThreshold { get; set; } = 3;

    /// <summary>
    /// Number of consecutive health check successes before marking instance as healthy
    /// Default: 2
    /// </summary>
    public int HealthCheckSuccessThreshold { get; set; } = 2;

    /// <summary>
    /// How long to keep deregistered instances in the registry before removing
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan InstanceTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to automatically export services to configured exporters on changes
    /// Default: true
    /// </summary>
    public bool AutoExport { get; set; } = true;

    /// <summary>
    /// Minimum interval between exports (debouncing)
    /// Default: 1 second
    /// </summary>
    public TimeSpan ExportDebounceInterval { get; set; } = TimeSpan.FromSeconds(1);
}
