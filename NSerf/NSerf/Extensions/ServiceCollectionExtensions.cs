// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NSerf.Extensions;

/// <summary>
/// Extension methods for configuring Serf services in dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Serf agent services to the service collection with default configuration.
    /// The agent will use the machine name as the node name and bind to 0.0.0.0:7946.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSerf(this IServiceCollection services)
    {
        return services.AddSerf(_ => { });
    }

    /// <summary>
    /// Adds Serf agent services to the service collection with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure Serf options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddSerf(options =>
    /// {
    ///     options.NodeName = "web-server-1";
    ///     options.BindAddr = "0.0.0.0:7946";
    ///     options.Tags["role"] = "web";
    ///     options.Tags["datacenter"] = "us-east-1";
    ///     options.StartJoin = new[] { "10.0.1.10:7946", "10.0.1.11:7946" };
    ///     options.SnapshotPath = "/var/serf/snapshot";
    ///     options.RejoinAfterLeave = true;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddSerf(
        this IServiceCollection services,
        Action<SerfOptions> configureOptions)
    {
        // Register options
        services.Configure(configureOptions);

        // Register SerfAgent as singleton
        services.AddSingleton<Agent.SerfAgent>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SerfOptions>>().Value;
            var logger = sp.GetService<ILogger<Agent.SerfAgent>>();
            var agentConfig = options.ToAgentConfig();

            return new Agent.SerfAgent(agentConfig, logger);
        });

        // Register Serf instance accessor (available after agent starts)
        services.AddSingleton<NSerf.Serf.Serf>(sp =>
        {
            var agent = sp.GetRequiredService<Agent.SerfAgent>();
            return agent.Serf ?? throw new InvalidOperationException(
                "Serf instance not available. Ensure the SerfAgent has been started.");
        });

        // Register hosted service for lifecycle management (only if not already registered)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SerfHostedService>());

        return services;
    }

    /// <summary>
    /// Adds Serf agent services with configuration from IConfiguration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="configurationSectionPath">Path to configuration section (e.g., "Serf").</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// appsettings.json:
    /// <code>
    /// {
    ///   "Serf": {
    ///     "NodeName": "web-server-1",
    ///     "BindAddr": "0.0.0.0:7946",
    ///     "Tags": {
    ///       "role": "web",
    ///       "datacenter": "us-east-1"
    ///     },
    ///     "StartJoin": ["10.0.1.10:7946", "10.0.1.11:7946"],
    ///     "SnapshotPath": "/var/serf/snapshot",
    ///     "RejoinAfterLeave": true
    ///   }
    /// }
    /// </code>
    /// 
    /// Startup:
    /// <code>
    /// services.AddSerf(configuration, "Serf");
    /// </code>
    /// </example>
    public static IServiceCollection AddSerf(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string configurationSectionPath = "Serf")
    {
        if (string.IsNullOrWhiteSpace(configurationSectionPath))
            throw new ArgumentException("Configuration section path cannot be empty", nameof(configurationSectionPath));

        // Bind configuration section to SerfOptions
        services.Configure<SerfOptions>(configuration.GetSection(configurationSectionPath));

        // Register SerfAgent as singleton
        services.AddSingleton<Agent.SerfAgent>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SerfOptions>>().Value;
            var logger = sp.GetService<ILogger<Agent.SerfAgent>>();
            var agentConfig = options.ToAgentConfig();

            return new Agent.SerfAgent(agentConfig, logger);
        });

        // Register Serf instance accessor
        services.AddSingleton<NSerf.Serf.Serf>(sp =>
        {
            var agent = sp.GetRequiredService<Agent.SerfAgent>();
            return agent.Serf ?? throw new InvalidOperationException(
                "Serf instance not available. Ensure the SerfAgent has been started.");
        });

        // Register hosted service for lifecycle management (only if not already registered)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SerfHostedService>());

        return services;
    }
}
