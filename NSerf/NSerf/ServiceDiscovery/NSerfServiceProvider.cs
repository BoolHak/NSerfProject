using Microsoft.Extensions.Logging;
using NSerf.Serf;
using NSerf.Serf.Events;
using System.Collections.Concurrent;

namespace NSerf.ServiceDiscovery;

/// <summary>
/// Service discovery provider that uses NSerf cluster membership as the data source.
/// Monitors Serf member events and tags to discover and track service instances.
/// </summary>
/// <remarks>
/// This provider treats each Serf member as a potential service instance.
/// Service metadata is extracted from member tags using configurable prefixes:
/// - "service:" tag indicates the service name
/// - "port:" tag indicates the service port
/// - "scheme:" tag indicates the protocol scheme (http/https/tcp/etc)
/// - "weight:" tag indicates load balancing weight
/// - Additional tags are stored as metadata
/// 
/// Example member tags:
/// {
///   "service:api": "true",
///   "port:api": "8080",
///   "scheme:api": "https",
///   "weight:api": "100",
///   "version": "1.2.3",
///   "region": "us-east-1"
/// }
/// </remarks>
public sealed class NSerfServiceProvider : IServiceProvider, IDisposable
{
    private readonly Serf.Serf _serf;
    private readonly ILogger? _logger;
    private readonly NSerfServiceProviderOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, ServiceInstance> _instances = new();
    private Task? _eventLoopTask;
    private bool _disposed;

    /// <inheritdoc/>
    public string Name => "NSerf";

    /// <inheritdoc/>
    public event EventHandler<ServiceChangedEventArgs>? ServiceDiscovered;

    /// <summary>
    /// Creates a new NSerf service provider.
    /// </summary>
    /// <param name="serf">The Serf instance to monitor for service discovery.</param>
    /// <param name="options">Configuration options for the provider.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public NSerfServiceProvider(Serf.Serf serf, NSerfServiceProviderOptions? options = null, ILogger? logger = null)
    {
        _serf = serf ?? throw new ArgumentNullException(nameof(serf));
        _options = options ?? new NSerfServiceProviderOptions();
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger?.LogInformation("[NSerfServiceProvider] Starting service discovery from Serf cluster");

        // Discover existing members immediately
        _ = Task.Run(async () =>
        {
            try
            {
                await DiscoverExistingMembersAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[NSerfServiceProvider] Failed to discover existing members");
            }
        }, cancellationToken);

        // Start an event loop to monitor member changes
        _eventLoopTask = Task.Factory.StartNew(
                () => EventLoopAsync(_cts.Token), 
                CancellationToken.None, 
                TaskCreationOptions.LongRunning, 
                TaskScheduler.Default
            ).Unwrap();

        _logger?.LogInformation("[NSerfServiceProvider] Service discovery started");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger?.LogInformation("[NSerfServiceProvider] Stopping service discovery");

        _cts.Cancel();

        if (_eventLoopTask != null)
        {
            try
            {
                _eventLoopTask.Wait(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        _logger?.LogInformation("[NSerfServiceProvider] Service discovery stopped");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Service>> DiscoverServicesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Group instances by service name
        var serviceGroups = _instances.Values
            .GroupBy(i => i.ServiceName)
            .Select(g => new Service
            {
                Name = g.Key,
                Instances = [.. g]
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<Service>>(serviceGroups);
    }

    /// <summary>
    /// Discovers services from existing Serf members.
    /// </summary>
    private async Task DiscoverExistingMembersAsync(CancellationToken cancellationToken)
    {
        var members = _serf.Members(MemberStatus.Alive);
        _logger?.LogInformation("[NSerfServiceProvider] Discovering services from {Count} alive members", members.Length);

        foreach (var member in members)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessMemberAsync(member, ServiceChangeType.InstanceRegistered);
        }
    }

    /// <summary>
    /// Event loop that monitors Serf member events.
    /// </summary>
    private async Task EventLoopAsync(CancellationToken cancellationToken)
    {
        _logger?.LogDebug("[NSerfServiceProvider] Event loop started");

        try
        {
            // Subscribe to Serf's IPC event channel
            var eventReader = _serf.IpcEventReader;

            await foreach (var evt in eventReader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await HandleSerfEventAsync(evt);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[NSerfServiceProvider] Error handling Serf event: {EventType}", evt.EventType());
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger?.LogDebug(ex, "[NSerfServiceProvider] Event loop cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[NSerfServiceProvider] Event loop terminated with error");
        }
    }

    /// <summary>
    /// Handles a Serf event and updates service discovery state.
    /// </summary>
    private async Task HandleSerfEventAsync(IEvent evt)
    {
        switch (evt)
        {
            case MemberEvent memberEvent:
                await HandleMemberEventAsync(memberEvent);
                break;

            case UserEvent userEvent when _options.EnableUserEventDiscovery:
                await HandleUserEventAsync(userEvent);
                break;
        }
    }

    /// <summary>
    /// Handles member join/leave/update events.
    /// </summary>
    private async Task HandleMemberEventAsync(MemberEvent memberEvent)
    {
        foreach (var member in memberEvent.Members)
        {
            var changeType = memberEvent.Type switch
            {
                EventType.MemberJoin => ServiceChangeType.InstanceRegistered,
                EventType.MemberLeave => ServiceChangeType.InstanceDeregistered,
                EventType.MemberFailed => ServiceChangeType.HealthStatusChanged,
                EventType.MemberUpdate => ServiceChangeType.InstanceUpdated,
                EventType.MemberReap => ServiceChangeType.InstanceDeregistered,
                _ => (ServiceChangeType?)null
            };

            if (changeType.HasValue)
            {
                await ProcessMemberAsync(member, changeType.Value);
            }
        }
    }

    /// <summary>
    /// Handles user events for dynamic service registration/deregistration.
    /// </summary>
    private Task HandleUserEventAsync(UserEvent userEvent)
    {
        // User events can be used for out-of-band service registration
        // Format: "service:register" or "service:deregister" with JSON payload
        // This is optional and controlled by the EnableUserEventDiscovery option

        if (!userEvent.Name.StartsWith(_options.UserEventPrefix, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        _logger?.LogDebug("[NSerfServiceProvider] Received service user event: {Name}", userEvent.Name);

        // Future: Implement user event-based service registration if needed
        // This would allow services to register without being a Serf member

        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes a Serf member and extracts service information from tags.
    /// </summary>
    private async Task ProcessMemberAsync(Member member, ServiceChangeType changeType)
    {
        var services = ExtractServicesFromMember(member);

        foreach (var (serviceName, instance) in services)
        {
            var instanceKey = $"{member.Name}:{serviceName}";

            if (changeType == ServiceChangeType.InstanceDeregistered)
            {
                if (!_instances.TryRemove(instanceKey, out var removed)) continue;
                _logger?.LogInformation("[NSerfServiceProvider] Deregistered service instance: {Service}/{Instance}",
                    serviceName, member.Name);

                RaiseServiceDiscovered(new ServiceChangedEventArgs
                {
                    ChangeType = ServiceChangeType.InstanceDeregistered,
                    ServiceName = serviceName,
                    Instance = removed
                });
            }
            else
            {
                // Update health status based on member status
                var healthStatus = member.Status switch
                {
                    MemberStatus.Alive => InstanceHealthStatus.Healthy,
                    MemberStatus.Failed => InstanceHealthStatus.Unhealthy,
                    MemberStatus.Leaving => InstanceHealthStatus.Draining,
                    _ => InstanceHealthStatus.Unknown
                };

                var updatedInstance = instance with { HealthStatus = healthStatus };
                var isNew = !_instances.ContainsKey(instanceKey);

                _instances[instanceKey] = updatedInstance;

                ServiceChangeType actualChangeType;
                if (isNew)
                {
                    actualChangeType = ServiceChangeType.InstanceRegistered;
                }
                else if (changeType == ServiceChangeType.HealthStatusChanged)
                {
                    actualChangeType = ServiceChangeType.HealthStatusChanged;
                }
                else
                {
                    actualChangeType = ServiceChangeType.InstanceUpdated;
                }

                _logger?.LogInformation("[NSerfServiceProvider] {Action} service instance: {Service}/{Instance} [{Status}]",
                    isNew ? "Registered" : "Updated", serviceName, member.Name, healthStatus);

                RaiseServiceDiscovered(new ServiceChangedEventArgs
                {
                    ChangeType = actualChangeType,
                    ServiceName = serviceName,
                    Instance = updatedInstance
                });
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Extracts service information from a Serf member's tags.
    /// Returns a dictionary of service name to service instance.
    /// </summary>
    private Dictionary<string, ServiceInstance> ExtractServicesFromMember(Member member)
    {
        var services = new Dictionary<string, ServiceInstance>();

        if (member.Tags.Count == 0)
            return services;

        // Find all service tags (e.g., "service:api", "service:web")
        var serviceTagPrefix = _options.ServiceTagPrefix;
        var serviceTags = member.Tags
            .Where(kvp => kvp.Key.StartsWith(serviceTagPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var serviceTag in serviceTags)
        {
            // Extract service name from tag key (e.g., "service:api" -> "api")
            var serviceName = serviceTag.Key[serviceTagPrefix.Length..];
            if (string.IsNullOrWhiteSpace(serviceName))
                continue;

            // Extract port, scheme, weight from related tags
            var portTag = $"{_options.PortTagPrefix}{serviceName}";
            var schemeTag = $"{_options.SchemeTagPrefix}{serviceName}";
            var weightTag = $"{_options.WeightTagPrefix}{serviceName}";

            if (!member.Tags.TryGetValue(portTag, out var portStr) ||
                !int.TryParse(portStr, out var port))
            {
                _logger?.LogWarning("[NSerfServiceProvider] Member {Member} has service tag '{Service}' but no valid port tag '{PortTag}'",
                    member.Name, serviceName, portTag);
                continue;
            }

            var scheme = member.Tags.GetValueOrDefault(schemeTag, "http");
            var weight = member.Tags.TryGetValue(weightTag, out var weightStr) && int.TryParse(weightStr, out var w) ? w : 100;

            // Collect all other tags as metadata (excluding service-specific tags)
            var metadata = member.Tags
                .Where(kvp => !kvp.Key.StartsWith(serviceTagPrefix, StringComparison.OrdinalIgnoreCase) &&
                             !kvp.Key.StartsWith(_options.PortTagPrefix, StringComparison.OrdinalIgnoreCase) &&
                             !kvp.Key.StartsWith(_options.SchemeTagPrefix, StringComparison.OrdinalIgnoreCase) &&
                             !kvp.Key.StartsWith(_options.WeightTagPrefix, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // Add Serf-specific metadata
            metadata["serf.member.name"] = member.Name;
            metadata["serf.member.addr"] = member.Addr?.ToString() ?? "";
            metadata["serf.member.port"] = member.Port.ToString();
            metadata["serf.member.status"] = member.Status.ToString();

            var instance = new ServiceInstance
            {
                Id = $"{member.Name}:{serviceName}",
                ServiceName = serviceName,
                Host = member.Addr?.ToString() ?? "unknown",
                Port = port,
                Scheme = scheme,
                Weight = weight,
                HealthStatus = member.Status == MemberStatus.Alive ? InstanceHealthStatus.Healthy : InstanceHealthStatus.Unhealthy,
                Metadata = metadata,
                Timestamp = DateTimeOffset.UtcNow
            };

            services[serviceName] = instance;
        }

        return services;
    }

    /// <summary>
    /// Raises the ServiceDiscovered event with proper exception isolation.
    /// </summary>
    private void RaiseServiceDiscovered(ServiceChangedEventArgs args)
    {
        var handler = ServiceDiscovered;
        if (handler == null) return;

        foreach (var singleHandler in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<ServiceChangedEventArgs>)singleHandler).Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[NSerfServiceProvider] Error in ServiceDiscovered event handler");
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();

        _logger?.LogDebug("[NSerfServiceProvider] Disposed");
    }
}
