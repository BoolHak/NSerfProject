using Microsoft.Extensions.DependencyInjection;

namespace NSerf.ServiceDiscovery.Http;

/// <summary>
/// Extension methods for integrating service discovery with HttpClient.
/// </summary>
public static class NSerfServiceDiscoveryHttpExtensions
{
    /// <summary>
    /// Adds service discovery to all HTTP clients by default.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for service discovery HTTP options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddServiceDiscoveryHttpClient();
    /// 
    /// // Now all HTTP clients will resolve service names:
    /// var client = httpClientFactory.CreateClient();
    /// await client.GetAsync("http://api/users");
    /// </code>
    /// </example>
    public static IServiceCollection AddServiceDiscoveryHttpClient(
        this IServiceCollection services,
        Action<ServiceDiscoveryHttpOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Add a service discovery handler to all HTTP clients by default
        services.ConfigureHttpClientDefaults(builder =>
        {
            builder.AddHttpMessageHandler(sp =>
            {
                var registry = sp.GetRequiredService<IServiceRegistry>();
                var options = configureOptions != null
                    ? Microsoft.Extensions.Options.Options.Create(new ServiceDiscoveryHttpOptions())
                    : null;
                var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<ServiceDiscoveryHttpMessageHandler>>();

                return new ServiceDiscoveryHttpMessageHandler(registry, options?.Value, logger);
            });
        });

        return services;
    }

    /// <summary>
    /// Adds service discovery to a specific named HTTP client.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <param name="configureOptions">Optional configuration for service discovery HTTP options.</param>
    /// <returns>The HTTP client builder for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddHttpClient("api")
    ///     .AddServiceDiscovery(options => 
    ///     {
    ///         options.LoadBalancingStrategy = LoadBalancingStrategy.WeightedRandom;
    ///         options.FailOnNoEndpoints = true;
    ///     });
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddServiceDiscovery(
        this IHttpClientBuilder builder,
        Action<ServiceDiscoveryHttpOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddHttpMessageHandler(sp =>
        {
            var registry = sp.GetRequiredService<IServiceRegistry>();

            ServiceDiscoveryHttpOptions? options = null;
            if (configureOptions != null)
            {
                options = new ServiceDiscoveryHttpOptions();
                configureOptions(options);
            }

            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<ServiceDiscoveryHttpMessageHandler>>();

            return new ServiceDiscoveryHttpMessageHandler(registry, options, logger);
        });

        return builder;
    }
}
