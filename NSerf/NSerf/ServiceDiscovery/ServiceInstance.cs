namespace NSerf.ServiceDiscovery;

/// <summary>
/// Represents a single instance of a service (backend/destination).
/// Provider-agnostic representation of a service endpoint.
/// </summary>
public sealed record ServiceInstance
{
    /// <summary>
    /// Unique identifier for this instance (e.g., node name, container ID)
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Service name this instance belongs to
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// Host address (IP or hostname)
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// Port number
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Protocol/scheme (http, https, grpc, tcp, etc.)
    /// </summary>
    public string Scheme { get; init; } = "http";

    /// <summary>
    /// Current health status of this instance
    /// </summary>
    public InstanceHealthStatus HealthStatus { get; init; } = InstanceHealthStatus.Unknown;

    /// <summary>
    /// Weight for load balancing (default: 100)
    /// </summary>
    public int Weight { get; init; } = 100;

    /// <summary>
    /// Arbitrary metadata/tags associated with this instance
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// When this instance was registered/last updated
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the full address in format: scheme://host:port
    /// </summary>
    public string Address => $"{Scheme}://{Host}:{Port}";
}

/// <summary>
/// Health status of a service instance
/// </summary>
public enum InstanceHealthStatus
{
    /// <summary>
    /// Health status is unknown (not yet checked)
    /// </summary>
    Unknown,

    /// <summary>
    /// Instance is healthy and accepting traffic
    /// </summary>
    Healthy,

    /// <summary>
    /// Instance is unhealthy but still discoverable
    /// </summary>
    Unhealthy,

    /// <summary>
    /// Instance is being drained (graceful shutdown)
    /// </summary>
    Draining
}
