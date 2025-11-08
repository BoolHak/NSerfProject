namespace NSerf.ServiceDiscovery;

/// <summary>
/// Configuration options for the NSerf service discovery provider.
/// </summary>
public sealed class NSerfServiceProviderOptions
{
    /// <summary>
    /// Tag prefix used to identify service names in Serf member tags.
    /// Default: "service:"
    /// Example: "service:api" indicates the member provides an "api" service
    /// </summary>
    public string ServiceTagPrefix { get; init; } = "service:";

    /// <summary>
    /// Tag prefix used to identify service ports in Serf member tags.
    /// Default: "port:"
    /// Example: "port:api" = "8080"
    /// </summary>
    public string PortTagPrefix { get; init; } = "port:";

    /// <summary>
    /// Tag prefix used to identify service schemes in Serf member tags.
    /// Default: "scheme:"
    /// Example: "scheme:api" = "https"
    /// </summary>
    public string SchemeTagPrefix { get; init; } = "scheme:";

    /// <summary>
    /// Tag prefix used to identify service weights in Serf member tags.
    /// Default: "weight:"
    /// Example: "weight:api" = "100"
    /// </summary>
    public string WeightTagPrefix { get; init; } = "weight:";

    /// <summary>
    /// Whether to enable service discovery via Serf user events.
    /// When enabled, user events with the configured prefix can trigger service registration/deregistration.
    /// Default: false
    /// </summary>
    public bool EnableUserEventDiscovery { get; init; } = false;

    /// <summary>
    /// Prefix for user events that trigger service discovery actions.
    /// Default: "service:"
    /// Example: "service:register", "service:deregister"
    /// </summary>
    public string UserEventPrefix { get; init; } = "service:";

    /// <summary>
    /// Whether to automatically mark instances as unhealthy when their Serf member fails.
    /// Default: true
    /// </summary>
    public bool AutoMarkFailedUnhealthy { get; init; } = true;

    /// <summary>
    /// Whether to automatically deregister instances when their Serf member leaves.
    /// Default: true
    /// </summary>
    public bool AutoDeregisterOnLeave { get; init; } = true;
}
