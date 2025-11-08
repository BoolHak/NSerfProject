using Microsoft.Extensions.DependencyInjection;
using NSerf.ServiceDiscovery;
using NSerf.ServiceDiscovery.Http;
using Xunit;

namespace NSerfTests.ServiceDiscovery.Http;

/// <summary>
/// Tests for NSerfServiceDiscoveryHttpExtensions DI integration.
/// </summary>
public sealed class NSerfServiceDiscoveryHttpExtensionsTests
{
    [Fact]
    public void AddServiceDiscoveryHttpClient_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IServiceRegistry, ServiceRegistry>();
        services.AddLogging();

        // Act
        services.AddServiceDiscoveryHttpClient();
        var provider = services.BuildServiceProvider();

        // Assert
        var registry = provider.GetService<IServiceRegistry>();
        Assert.NotNull(registry);
    }

    [Fact]
    public void AddServiceDiscoveryHttpClient_WithOptions_ConfiguresOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IServiceRegistry, ServiceRegistry>();
        services.AddLogging();

        // Act
        services.AddServiceDiscoveryHttpClient(options =>
        {
            options.LoadBalancingStrategy = LoadBalancingStrategy.WeightedRandom;
            options.FailOnNoEndpoints = true;
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<Microsoft.Extensions.Options.IOptions<ServiceDiscoveryHttpOptions>>();
        Assert.NotNull(options);
    }

    [Fact]
    public void AddServiceDiscovery_OnHttpClientBuilder_AddsHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IServiceRegistry, ServiceRegistry>();
        services.AddLogging();

        // Act
        services.AddHttpClient("test")
            .AddServiceDiscovery();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // Assert
        var client = factory.CreateClient("test");
        Assert.NotNull(client);
    }

    [Fact]
    public void AddServiceDiscovery_WithOptions_ConfiguresPerClient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IServiceRegistry, ServiceRegistry>();
        services.AddLogging();

        // Act
        services.AddHttpClient("test")
            .AddServiceDiscovery(options =>
            {
                options.LoadBalancingStrategy = LoadBalancingStrategy.RoundRobin;
            });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // Assert
        var client = factory.CreateClient("test");
        Assert.NotNull(client);
    }

    [Fact]
    public void AddServiceDiscoveryHttpClient_NullServices_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddServiceDiscoveryHttpClient());
    }

    [Fact]
    public void AddServiceDiscovery_NullBuilder_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ((IHttpClientBuilder)null!).AddServiceDiscovery());
    }
}
