namespace NSerf.ServiceDiscovery;

/// <summary>
/// Represents a logical service with all its instances.
/// Provider-agnostic representation of a distributed service.
/// </summary>
public sealed record Service
{
    /// <summary>
    /// Unique service name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// All instances (endpoints) for this service
    /// </summary>
    public required IReadOnlyList<ServiceInstance> Instances { get; init; }

    /// <summary>
    /// Service-level metadata/tags
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// When this service was last updated
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Total number of instances
    /// </summary>
    public int InstanceCount => Instances.Count;

    /// <summary>
    /// Number of healthy instances
    /// </summary>
    public int HealthyInstanceCount => Instances.Count(i => i.HealthStatus == InstanceHealthStatus.Healthy);

    /// <summary>
    /// Whether this service has any healthy instances
    /// </summary>
    public bool HasHealthyInstances => HealthyInstanceCount > 0;
}
