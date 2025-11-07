namespace NSerf.ServiceDiscovery;

/// <summary>
/// Core interface for service discovery registry.
/// Provides a generic abstraction for service registration and discovery.
/// </summary>
public interface IServiceRegistry
{
    /// <summary>
    /// Gets all registered services
    /// </summary>
    IReadOnlyList<Service> GetServices();

    /// <summary>
    /// Gets a specific service by name
    /// </summary>
    Service? GetService(string serviceName);

    /// <summary>
    /// Gets all instances of a specific service
    /// </summary>
    IReadOnlyList<ServiceInstance> GetInstances(string serviceName);

    /// <summary>
    /// Gets all healthy instances of a specific service
    /// </summary>
    IReadOnlyList<ServiceInstance> GetHealthyInstances(string serviceName);

    /// <summary>
    /// Registers or updates a service instance
    /// </summary>
    Task RegisterInstanceAsync(ServiceInstance instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deregisters a service instance
    /// </summary>
    Task DeregisterInstanceAsync(string serviceName, string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the health status of an instance
    /// </summary>
    Task UpdateHealthStatusAsync(string serviceName, string instanceId, InstanceHealthStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when services change (instance added/removed/updated)
    /// </summary>
    event EventHandler<ServiceChangedEventArgs>? ServiceChanged;
}

/// <summary>
/// Event args for service change notifications
/// </summary>
public sealed class ServiceChangedEventArgs : EventArgs
{
    /// <summary>
    /// Type of change that occurred
    /// </summary>
    public required ServiceChangeType ChangeType { get; init; }

    /// <summary>
    /// Name of the service that changed
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// The service instance affected (if applicable)
    /// </summary>
    public ServiceInstance? Instance { get; init; }

    /// <summary>
    /// Timestamp of the change
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Types of service changes
/// </summary>
public enum ServiceChangeType
{
    /// <summary>
    /// A new instance was registered
    /// </summary>
    InstanceRegistered,

    /// <summary>
    /// An instance was deregistered
    /// </summary>
    InstanceDeregistered,

    /// <summary>
    /// An instance's health status changed
    /// </summary>
    HealthStatusChanged,

    /// <summary>
    /// An instance's metadata was updated
    /// </summary>
    InstanceUpdated
}
