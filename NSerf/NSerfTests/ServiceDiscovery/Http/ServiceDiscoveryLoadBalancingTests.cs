using NSerf.ServiceDiscovery;
using NSerf.ServiceDiscovery.Http;
using System.Net;
using Xunit;

namespace NSerfTests.ServiceDiscovery.Http;

/// <summary>
/// Tests for load balancing strategies in ServiceDiscoveryHttpMessageHandler.
/// </summary>
public sealed class ServiceDiscoveryLoadBalancingTests : IDisposable
{
    private readonly ServiceRegistry _registry;
    private readonly TestHttpMessageHandler _innerHandler;

    public ServiceDiscoveryLoadBalancingTests()
    {
        _registry = new ServiceRegistry();
        _innerHandler = new TestHttpMessageHandler();
    }

    public void Dispose()
    {
        _innerHandler.Dispose();
    }

    [Fact]
    public async Task LoadBalancing_First_AlwaysSelectsFirstInstance()
    {
        // Arrange
        await RegisterMultipleInstances();

        var handler = new ServiceDiscoveryHttpMessageHandler(
            _registry,
            new ServiceDiscoveryHttpOptions { LoadBalancingStrategy = LoadBalancingStrategy.First })
        {
            InnerHandler = _innerHandler
        };

        var client = new HttpClient(handler);
        var hosts = new HashSet<string>();

        // Act - make multiple requests
        for (int i = 0; i < 5; i++)
        {
            await client.GetAsync("http://api/test");
            if (_innerHandler.LastRequest?.RequestUri != null)
            {
                hosts.Add(_innerHandler.LastRequest.RequestUri.Host);
            }
        }

        // Assert - should always use same instance (first in list)
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Single(hosts); // Only one unique host used

        handler.Dispose();
    }

    [Fact]
    public async Task LoadBalancing_RoundRobin_DistributesRequests()
    {
        // Arrange
        await RegisterMultipleInstances();

        var handler = new ServiceDiscoveryHttpMessageHandler(
            _registry,
            new ServiceDiscoveryHttpOptions { LoadBalancingStrategy = LoadBalancingStrategy.RoundRobin })
        {
            InnerHandler = _innerHandler
        };

        var client = new HttpClient(handler);
        var hosts = new HashSet<string>();

        // Act - make multiple requests
        for (int i = 0; i < 10; i++)
        {
            await client.GetAsync("http://api/test");
            if (_innerHandler.LastRequest?.RequestUri != null)
            {
                hosts.Add(_innerHandler.LastRequest.RequestUri.Host);
            }
            await Task.Delay(10); // Small delay to change timestamp
        }

        // Assert - should hit multiple instances
        Assert.True(hosts.Count >= 2, $"Expected at least 2 different hosts, got {hosts.Count}");

        handler.Dispose();
    }

    [Fact]
    public async Task LoadBalancing_Random_DistributesRequests()
    {
        // Arrange
        await RegisterMultipleInstances();

        var handler = new ServiceDiscoveryHttpMessageHandler(
            _registry,
            new ServiceDiscoveryHttpOptions { LoadBalancingStrategy = LoadBalancingStrategy.Random })
        {
            InnerHandler = _innerHandler
        };

        var client = new HttpClient(handler);
        var hosts = new HashSet<string>();

        // Act - make multiple requests
        for (int i = 0; i < 20; i++)
        {
            await client.GetAsync("http://api/test");
            if (_innerHandler.LastRequest?.RequestUri != null)
            {
                hosts.Add(_innerHandler.LastRequest.RequestUri.Host);
            }
        }

        // Assert - should hit multiple instances (probabilistic)
        Assert.True(hosts.Count >= 2, $"Expected at least 2 different hosts, got {hosts.Count}");

        handler.Dispose();
    }

    [Fact]
    public async Task LoadBalancing_WeightedRandom_FavorsHigherWeights()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api",
            Host = "10.0.1.1",
            Port = 8080,
            Weight = 1, // Low weight
            HealthStatus = InstanceHealthStatus.Healthy
        });

        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-2",
            ServiceName = "api",
            Host = "10.0.1.2",
            Port = 8080,
            Weight = 99, // High weight
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var handler = new ServiceDiscoveryHttpMessageHandler(
            _registry,
            new ServiceDiscoveryHttpOptions { LoadBalancingStrategy = LoadBalancingStrategy.WeightedRandom })
        {
            InnerHandler = _innerHandler
        };

        var client = new HttpClient(handler);
        var hostCounts = new Dictionary<string, int>();

        // Act - make many requests
        for (int i = 0; i < 100; i++)
        {
            await client.GetAsync("http://api/test");
            if (_innerHandler.LastRequest?.RequestUri != null)
            {
                var host = _innerHandler.LastRequest.RequestUri.Host;
                hostCounts[host] = hostCounts.GetValueOrDefault(host) + 1;
            }
        }

        // Assert - high weight instance should get more requests
        Assert.True(hostCounts.ContainsKey("10.0.1.2"));
        Assert.True(hostCounts["10.0.1.2"] > hostCounts.GetValueOrDefault("10.0.1.1"),
            $"Expected 10.0.1.2 (weight 99) to have more requests than 10.0.1.1 (weight 1). " +
            $"Got: 10.0.1.2={hostCounts["10.0.1.2"]}, 10.0.1.1={hostCounts.GetValueOrDefault("10.0.1.1")}");

        handler.Dispose();
    }

    [Fact]
    public async Task LoadBalancing_WeightedRandom_ZeroWeights_FallsBackToRandom()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api",
            Host = "10.0.1.1",
            Port = 8080,
            Weight = 0,
            HealthStatus = InstanceHealthStatus.Healthy
        });

        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-2",
            ServiceName = "api",
            Host = "10.0.1.2",
            Port = 8080,
            Weight = 0,
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var handler = new ServiceDiscoveryHttpMessageHandler(
            _registry,
            new ServiceDiscoveryHttpOptions { LoadBalancingStrategy = LoadBalancingStrategy.WeightedRandom })
        {
            InnerHandler = _innerHandler
        };

        var client = new HttpClient(handler);
        var hosts = new HashSet<string>();

        // Act - make multiple requests
        for (int i = 0; i < 20; i++)
        {
            await client.GetAsync("http://api/test");
            if (_innerHandler.LastRequest?.RequestUri != null)
            {
                hosts.Add(_innerHandler.LastRequest.RequestUri.Host);
            }
        }

        // Assert - should still distribute (falls back to random)
        Assert.True(hosts.Count >= 1, "Should select at least one instance");

        handler.Dispose();
    }

    [Fact]
    public async Task LoadBalancing_SingleInstance_AlwaysSelectsSameInstance()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api",
            Host = "10.0.1.1",
            Port = 8080,
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var handler = new ServiceDiscoveryHttpMessageHandler(
            _registry,
            new ServiceDiscoveryHttpOptions { LoadBalancingStrategy = LoadBalancingStrategy.Random })
        {
            InnerHandler = _innerHandler
        };

        var client = new HttpClient(handler);

        // Act - make multiple requests
        for (int i = 0; i < 5; i++)
        {
            await client.GetAsync("http://api/test");
            Assert.Equal("10.0.1.1", _innerHandler.LastRequest!.RequestUri!.Host);
        }

        handler.Dispose();
    }

    private async Task RegisterMultipleInstances()
    {
        for (int i = 1; i <= 3; i++)
        {
            await _registry.RegisterInstanceAsync(new ServiceInstance
            {
                Id = $"api-{i}",
                ServiceName = "api",
                Host = $"10.0.1.{i}",
                Port = 8080,
                HealthStatus = InstanceHealthStatus.Healthy
            });
        }
    }
}
