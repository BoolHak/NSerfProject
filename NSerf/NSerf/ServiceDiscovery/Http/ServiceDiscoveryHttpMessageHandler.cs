using Microsoft.Extensions.Logging;
using System.Net;

namespace NSerf.ServiceDiscovery.Http;

/// <summary>
/// HTTP message handler that resolves service names to actual endpoints using the service registry.
/// Integrates with HttpClientFactory to enable service discovery for HTTP clients.
/// </summary>
/// <remarks>
/// This handler intercepts HTTP requests and resolves logical service names to physical endpoints.
/// 
/// Example:
/// <code>
/// var client = httpClientFactory.CreateClient();
/// var response = await client.GetAsync("http://api/users");
/// // "api" is resolved to actual endpoint like "http://10.0.1.5:8080/users"
/// </code>
/// </remarks>
public sealed class ServiceDiscoveryHttpMessageHandler : DelegatingHandler
{
    private readonly IServiceRegistry _registry;
    private readonly ILogger<ServiceDiscoveryHttpMessageHandler>? _logger;
    private readonly ServiceDiscoveryHttpOptions _options;

    /// <summary>
    /// Creates a new service discovery HTTP message handler.
    /// </summary>
    /// <param name="registry">The service registry to use for endpoint resolution.</param>
    /// <param name="options">Configuration options for the handler.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public ServiceDiscoveryHttpMessageHandler(
        IServiceRegistry registry,
        ServiceDiscoveryHttpOptions? options = null,
        ILogger<ServiceDiscoveryHttpMessageHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
        _options = options ?? new ServiceDiscoveryHttpOptions();
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestUri == null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var originalUri = request.RequestUri;
        var serviceName = originalUri.Host;

        // Check if this looks like a service name (not an IP or FQDN)
        if (!ShouldResolve(serviceName))
        {
            _logger?.LogTrace("Skipping resolution for {Host} - not a service name", serviceName);
            return await base.SendAsync(request, cancellationToken);
        }

        // Resolve service to endpoint
        var endpoint = await ResolveEndpointAsync(serviceName);
        if (endpoint == null)
        {
            _logger?.LogWarning("No healthy instances found for service {ServiceName}", serviceName);

            if (_options.FailOnNoEndpoints)
            {
                throw new ServiceDiscoveryException($"No healthy instances found for service '{serviceName}'");
            }

            // Fall back to original URI (might be DNS resolvable)
            return await base.SendAsync(request, cancellationToken);
        }

        // Rewrite the request URI
        var resolvedUri = BuildResolvedUri(originalUri, endpoint);
        request.RequestUri = resolvedUri;

        _logger?.LogDebug(
            "Resolved service {ServiceName} from {OriginalUri} to {ResolvedUri}",
            serviceName,
            originalUri,
            resolvedUri);

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Request to {ServiceName} failed (endpoint: {Endpoint})",
                serviceName,
                endpoint.Address);
            throw;
        }
    }

    private bool ShouldResolve(string host)
    {
        // Don't resolve if it's an IP address
        if (IPAddress.TryParse(host, out _))
        {
            return false;
        }

        // Don't resolve if it contains dots (FQDN) unless explicitly allowed
        if (host.Contains('.') && !_options.ResolveFqdns)
        {
            return false;
        }

        // Don't resolve localhost
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private Task<ServiceInstance?> ResolveEndpointAsync(
        string serviceName)
    {
        var instances = _registry.GetHealthyInstances(serviceName);

        if (instances.Count == 0)
        {
            return Task.FromResult<ServiceInstance?>(null);
        }

        // Use load balancing strategy
        var selected = _options.LoadBalancingStrategy switch
        {
            LoadBalancingStrategy.RoundRobin => SelectRoundRobin(instances),
            LoadBalancingStrategy.Random => SelectRandom(instances),
            LoadBalancingStrategy.WeightedRandom => SelectWeightedRandom(instances),
            _ => instances[0]
        };

        return Task.FromResult<ServiceInstance?>(selected);
    }

    private static ServiceInstance SelectRoundRobin(IReadOnlyList<ServiceInstance> instances)
    {
        // Simple round-robin using timestamp
        var index = (int)(DateTime.UtcNow.Ticks % instances.Count);
        return instances[index];
    }

    private static ServiceInstance SelectRandom(IReadOnlyList<ServiceInstance> instances)
    {
        var index = Random.Shared.Next(instances.Count);
        return instances[index];
    }

    private static ServiceInstance SelectWeightedRandom(IReadOnlyList<ServiceInstance> instances)
    {
        var totalWeight = instances.Sum(i => i.Weight);
        if (totalWeight == 0)
        {
            return SelectRandom(instances);
        }

        var randomWeight = Random.Shared.Next(totalWeight);
        var currentWeight = 0;

        foreach (var instance in instances)
        {
            currentWeight += instance.Weight;
            if (randomWeight < currentWeight)
            {
                return instance;
            }
        }

        return instances[^1];
    }

    private static Uri BuildResolvedUri(Uri originalUri, ServiceInstance endpoint)
    {
        var builder = new UriBuilder(originalUri)
        {
            Host = endpoint.Host,
            Port = endpoint.Port
        };

        // Use the endpoint's scheme if specified, otherwise keep the original
        if (!string.IsNullOrEmpty(endpoint.Scheme))
        {
            builder.Scheme = endpoint.Scheme;
        }

        return builder.Uri;
    }
}

/// <summary>
/// Configuration options for service discovery HTTP message handler.
/// </summary>
public sealed class ServiceDiscoveryHttpOptions
{
    /// <summary>
    /// Load balancing strategy to use when multiple healthy instances are available.
    /// Default is RoundRobin.
    /// </summary>
    public LoadBalancingStrategy LoadBalancingStrategy { get; set; } = LoadBalancingStrategy.RoundRobin;

    /// <summary>
    /// Whether to resolve FQDNs (hosts with dots) as service names.
    /// Default is false.
    /// </summary>
    public bool ResolveFqdns { get; init; }

    /// <summary>
    /// Whether to throw an exception when no healthy endpoints are found.
    /// If false, it falls back to the original URI.
    /// Default is false.
    /// </summary>
    public bool FailOnNoEndpoints { get; set; }
}

/// <summary>
/// Load balancing strategies for selecting service instances.
/// </summary>
public enum LoadBalancingStrategy
{
    /// <summary>
    /// Select the first available instance.
    /// </summary>
    First,

    /// <summary>
    /// Round-robin selection based on current time.
    /// </summary>
    RoundRobin,

    /// <summary>
    /// Random selection with equal probability.
    /// </summary>
    Random,

    /// <summary>
    /// Weighted random selection based on instance weights.
    /// </summary>
    WeightedRandom
}

/// <summary>
/// Exception thrown when service discovery fails.
/// </summary>
public sealed class ServiceDiscoveryException : Exception
{
    /// <summary>
    /// Creates a new service discovery exception.
    /// </summary>
    public ServiceDiscoveryException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new service discovery exception with an inner exception.
    /// </summary>
    public ServiceDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
