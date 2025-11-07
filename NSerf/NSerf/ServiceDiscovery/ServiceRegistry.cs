using System.Collections.Concurrent;

namespace NSerf.ServiceDiscovery;

/// <summary>
/// In-memory implementation of IServiceRegistry.
/// Thread-safe service registry with event notifications using ReaderWriterLockSlim for optimal read concurrency.
/// </summary>
/// <remarks>
/// This registry uses a two-level concurrent dictionary structure:
/// - First level: Service name → Instance dictionary
/// - Second level: Instance ID → ServiceInstance
/// 
/// Concurrency model:
/// - Read operations (Get*) use read locks allowing multiple concurrent readers
/// - Write operations (Register/Deregister/Update) use write locks for exclusive access
/// - Events are raised inside writing locks, but handlers execute outside to prevent deadlock
/// 
/// Thread-safety: All public methods are thread-safe and can be called concurrently.
/// </remarks>
public sealed class ServiceRegistry : IServiceRegistry, IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ServiceInstance>> _services = new();
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>
    /// Event raised when a service instance is registered, deregistered, or its health status changes.
    /// Handlers execute outside of locks to prevent deadlocks.
    /// </summary>
    public event EventHandler<ServiceChangedEventArgs>? ServiceChanged;

    /// <summary>
    /// Gets all registered services with their instances.
    /// </summary>
    /// <returns>A snapshot of all services at the time of the call. Changes after this call won't affect the returned list.</returns>
    /// <remarks>
    /// This method acquires a read lock, allowing multiple concurrent calls.
    /// The returned list is a snapshot and safe to iterate without additional synchronization.
    /// </remarks>
    public IReadOnlyList<Service> GetServices()
    {
        _lock.EnterReadLock();
        try
        {
            return _services.Select(kvp => new Service
            {
                Name = kvp.Key,
                Instances = kvp.Value.Values.ToList(),
                LastUpdated = kvp.Value.Values.Max(i => i.Timestamp)
            }).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets a specific service by name with all its instances.
    /// </summary>
    /// <param name="serviceName">The name of the service to retrieve.</param>
    /// <returns>The service with all instances, or null if the service doesn't exist or has no instances.</returns>
    /// <remarks>
    /// This method acquires a read lock, allowing multiple concurrent calls.
    /// Returns null if the service has no registered instances.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when serviceName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when serviceName is empty or whitespace.</exception>
    public Service? GetService(string? serviceName)
    {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        
        _lock.EnterReadLock();
        try
        {
            if (!_services.TryGetValue(serviceName, out var instances) || instances.IsEmpty)
                return null;

            return new Service
            {
                Name = serviceName,
                Instances = instances.Values.ToList(),
                LastUpdated = instances.Values.Max(i => i.Timestamp)
            };
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets all instances of a specific service, regardless of health status.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <returns>A list of all instances, or an empty list if the service doesn't exist.</returns>
    /// <remarks>
    /// This method acquires a read lock, allowing multiple concurrent calls.
    /// Use <see cref="GetHealthyInstances"/> to get only healthy instances.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when serviceName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when serviceName is empty or whitespace.</exception>
    public IReadOnlyList<ServiceInstance> GetInstances(string? serviceName)
    {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        
        _lock.EnterReadLock();
        try
        {
            return _services.TryGetValue(serviceName, out var instances)
                ? instances.Values.ToList()
                : Array.Empty<ServiceInstance>();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets only the healthy instances of a specific service.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <returns>A list of healthy instances, or an empty list if the service doesn't exist or has no healthy instances.</returns>
    /// <remarks>
    /// This method acquires a read lock, allowing multiple concurrent calls.
    /// Only instances with <see cref="InstanceHealthStatus.Healthy"/> status are returned.
    /// Use this for load balancing and routing decisions.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when serviceName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when serviceName is empty or whitespace.</exception>
    public IReadOnlyList<ServiceInstance> GetHealthyInstances(string? serviceName)
    {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        
        _lock.EnterReadLock();
        try
        {
            return _services.TryGetValue(serviceName, out var instances)
                ? instances.Values.Where(i => i.HealthStatus == InstanceHealthStatus.Healthy).ToList()
                : Array.Empty<ServiceInstance>();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Registers or updates a service instance in the registry.
    /// </summary>
    /// <param name="instance">The service instance to register.</param>
    /// <param name="cancellationToken">Cancellation token (currently unused but provided for future async operations).</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// This method acquires a write lock for exclusive access.
    /// If an instance with the same ID already exists, it will be updated.
    /// Raises <see cref="ServiceChanged"/> event with <see cref="ServiceChangeType.InstanceRegistered"/> for new instances
    /// or <see cref="ServiceChangeType.InstanceUpdated"/> for existing instances.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when the instance is null.</exception>
    /// <exception cref="ArgumentException">Thrown when instance.ServiceName or instance.Id is empty or whitespace.</exception>
    public Task RegisterInstanceAsync(ServiceInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(instance.ServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instance.Id);
        
        _lock.EnterWriteLock();
        try
        {
            var instances = _services.GetOrAdd(instance.ServiceName, _ => new ConcurrentDictionary<string, ServiceInstance>());
            var isNew = !instances.ContainsKey(instance.Id);
            
            instances[instance.Id] = instance;

            RaiseServiceChanged(new ServiceChangedEventArgs
            {
                ChangeType = isNew ? ServiceChangeType.InstanceRegistered : ServiceChangeType.InstanceUpdated,
                ServiceName = instance.ServiceName,
                Instance = instance
            });
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Deregisters a service instance from the registry.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="instanceId">The ID of the instance to deregister.</param>
    /// <param name="cancellationToken">Cancellation token (currently unused but provided for future async operations).</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// This method acquires a write lock for exclusive access.
    /// If the instance doesn't exist, this method does nothing (idempotent).
    /// If this was the last instance of a service, the service entry is removed from the registry.
    /// Raises <see cref="ServiceChanged"/> event with <see cref="ServiceChangeType.InstanceDeregistered"/> if the instance existed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when serviceName or instanceId is null.</exception>
    /// <exception cref="ArgumentException">Thrown when serviceName or instanceId is empty or whitespace.</exception>
    public Task DeregisterInstanceAsync(string? serviceName, string? instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        
        _lock.EnterWriteLock();
        try
        {
            if (_services.TryGetValue(serviceName, out var instances) &&
                instances.TryRemove(instanceId, out var removed))
            {
                // Clean up empty service entries
                if (instances.IsEmpty)
                {
                    _services.TryRemove(serviceName, out _);
                }

                RaiseServiceChanged(new ServiceChangedEventArgs
                {
                    ChangeType = ServiceChangeType.InstanceDeregistered,
                    ServiceName = serviceName,
                    Instance = removed
                });
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates the health status of a service instance.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="instanceId">The ID of the instance to update.</param>
    /// <param name="status">The new health status.</param>
    /// <param name="cancellationToken">Cancellation token (currently unused but provided for future async operations).</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// This method acquires a write lock for exclusive access.
    /// If the instance doesn't exist, this method does nothing (idempotent).
    /// The instance's timestamp is automatically updated to the current time.
    /// Raises <see cref="ServiceChanged"/> event with <see cref="ServiceChangeType.HealthStatusChanged"/> if the instance existed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when serviceName or instanceId is null.</exception>
    /// <exception cref="ArgumentException">Thrown when serviceName or instanceId is empty or whitespace.</exception>
    public Task UpdateHealthStatusAsync(string? serviceName, string? instanceId, InstanceHealthStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        
        _lock.EnterWriteLock();
        try
        {
            if (_services.TryGetValue(serviceName, out var instances) &&
                instances.TryGetValue(instanceId, out var instance))
            {
                var updated = instance with { HealthStatus = status, Timestamp = DateTimeOffset.UtcNow };
                instances[instanceId] = updated;

                RaiseServiceChanged(new ServiceChangedEventArgs
                {
                    ChangeType = ServiceChangeType.HealthStatusChanged,
                    ServiceName = serviceName,
                    Instance = updated
                });
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Raises the ServiceChanged event.
    /// </summary>
    /// <param name="args">The event arguments.</param>
    /// <remarks>
    /// This method is called inside write locks, but event handlers execute outside to prevent deadlocks.
    /// Event handlers should not perform long-running operations to avoid blocking the registry.
    /// Exceptions from event handlers are caught and ignored to prevent one handler from breaking others.
    /// </remarks>
    private void RaiseServiceChanged(ServiceChangedEventArgs args)
    {
        var handler = ServiceChanged;
        if (handler == null) return;

        // Get an invocation list to call each handler independently
        foreach (var singleHandler in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<ServiceChangedEventArgs>)singleHandler).Invoke(this, args);
            }
            catch
            {
                // Swallow exceptions from event handlers to prevent one bad handler
                // from breaking the entire event chain or the registry
            }
        }
    }

    /// <summary>
    /// Disposes the registry and releases the underlying ReaderWriterLockSlim.
    /// </summary>
    /// <remarks>
    /// After disposal, the registry should not be used.
    /// This method does not clear registered services; call this only when shutting down.
    /// </remarks>
    public void Dispose()
    {
        _lock.Dispose();
    }
}
