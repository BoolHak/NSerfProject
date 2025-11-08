using NSerf.ServiceDiscovery;
using NSerf.Serf;
using Xunit.Abstractions;

namespace NSerfTests.ServiceDiscovery;

/// <summary>
/// Integration tests for NSerfServiceProvider with real Serf instances.
/// These tests verify end-to-end functionality with actual cluster operations.
/// </summary>
[Collection("Sequential")] // Run sequentially to avoid port conflicts
public class NSerfServiceProviderIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<NSerf.Serf.Serf> _serfInstances = [];
    private readonly List<NSerfServiceProvider> _providers = [];

    public NSerfServiceProviderIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SingleNode_WithServiceTags_DiscoversService()
    {
        // Arrange
        var config = CreateSerfConfig("node1", 17946, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["scheme:api"] = "https",
            ["weight:api"] = "150",
            ["version"] = "1.0.0"
        });

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        var provider = new NSerfServiceProvider(serf);
        _providers.Add(provider);

        var discoveredServices = new List<ServiceChangedEventArgs>();
        provider.ServiceDiscovered += (s, e) => discoveredServices.Add(e);

        // Act
        await provider.StartAsync();
        await Task.Delay(500); // Allow time for discovery

        var services = await provider.DiscoverServicesAsync();

        // Assert
        Assert.Single(services);
        var apiService = services.First(s => s.Name == "api");
        Assert.Single(apiService.Instances);

        var instance = apiService.Instances[0];
        Assert.Equal("node1:api", instance.Id);
        Assert.Equal("api", instance.ServiceName);
        Assert.Equal(8080, instance.Port);
        Assert.Equal("https", instance.Scheme);
        Assert.Equal(150, instance.Weight);
        Assert.Equal(InstanceHealthStatus.Healthy, instance.HealthStatus);
        Assert.Equal("1.0.0", instance.Metadata["version"]);
    }

    [Fact]
    public async Task SingleNode_MultipleServices_DiscoversAll()
    {
        // Arrange
        var config = CreateSerfConfig("node1", 17947, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["service:metrics"] = "true",
            ["port:metrics"] = "9090",
            ["service:health"] = "true",
            ["port:health"] = "8081"
        });

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        var provider = new NSerfServiceProvider(serf);
        _providers.Add(provider);

        // Act
        await provider.StartAsync();
        await Task.Delay(500);

        var services = await provider.DiscoverServicesAsync();

        // Assert
        Assert.Equal(3, services.Count);
        Assert.Contains(services, s => s.Name == "api");
        Assert.Contains(services, s => s.Name == "metrics");
        Assert.Contains(services, s => s.Name == "health");

        // Verify each service has exactly one instance
        Assert.All(services, s => Assert.Single(s.Instances));
    }

    [Fact]
    public async Task SingleNode_NoServiceTags_DiscoversNothing()
    {
        // Arrange
        var config = CreateSerfConfig("node1", 17948, new Dictionary<string, string>
        {
            ["role"] = "worker",
            ["datacenter"] = "us-east-1"
            // No service tags
        });

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        var provider = new NSerfServiceProvider(serf);
        _providers.Add(provider);

        // Act
        await provider.StartAsync();
        await Task.Delay(500);

        var services = await provider.DiscoverServicesAsync();

        // Assert
        Assert.Empty(services);
    }

    [Fact]
    public async Task CustomTagPrefixes_DiscoversCorrectly()
    {
        // Arrange
        var config = CreateSerfConfig("node1", 17949, new Dictionary<string, string>
        {
            ["svc:api"] = "true",
            ["p:api"] = "8080",
            ["proto:api"] = "grpc",
            ["w:api"] = "200"
        });

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        var options = new NSerfServiceProviderOptions
        {
            ServiceTagPrefix = "svc:",
            PortTagPrefix = "p:",
            SchemeTagPrefix = "proto:",
            WeightTagPrefix = "w:"
        };

        var provider = new NSerfServiceProvider(serf, options);
        _providers.Add(provider);

        // Act
        await provider.StartAsync();
        await Task.Delay(500);

        var services = await provider.DiscoverServicesAsync();

        // Assert
        Assert.Single(services);
        var instance = services[0].Instances[0];
        Assert.Equal(8080, instance.Port);
        Assert.Equal("grpc", instance.Scheme);
        Assert.Equal(200, instance.Weight);
    }

    [Fact]
    public async Task ServiceDiscoveredEvent_RaisedOnStart()
    {
        // Arrange
        var config = CreateSerfConfig("node1", 17950, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        });

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        var provider = new NSerfServiceProvider(serf);
        _providers.Add(provider);

        var eventRaised = false;
        ServiceChangedEventArgs? capturedArgs = null;

        provider.ServiceDiscovered += (s, e) =>
        {
            eventRaised = true;
            capturedArgs = e;
        };

        // Act
        await provider.StartAsync();
        await Task.Delay(1000); // Give time for event processing

        // Assert
        Assert.True(eventRaised, "ServiceDiscovered event should be raised");
        if (capturedArgs != null)
        {
            // Event type can be InstanceRegistered or InstanceUpdated depending on timing
            Assert.True(
                capturedArgs.ChangeType == ServiceChangeType.InstanceRegistered ||
                capturedArgs.ChangeType == ServiceChangeType.InstanceUpdated,
                $"Expected InstanceRegistered or InstanceUpdated, got {capturedArgs.ChangeType}");
            Assert.Equal("api", capturedArgs.ServiceName);
            Assert.NotNull(capturedArgs.Instance);
        }
    }

    [Fact]
    public async Task ProviderLifecycle_StartStopRestart_WorksCorrectly()
    {
        // Arrange
        var config = CreateSerfConfig("node1", 17951, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        });

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        var provider = new NSerfServiceProvider(serf);
        _providers.Add(provider);

        // Act & Assert - First cycle
        await provider.StartAsync();
        await Task.Delay(300);
        var services1 = await provider.DiscoverServicesAsync();
        Assert.Single(services1);

        await provider.StopAsync();
        await Task.Delay(300);

        // Second cycle
        await provider.StartAsync();
        await Task.Delay(300);
        var services2 = await provider.DiscoverServicesAsync();
        Assert.Single(services2);

        await provider.StopAsync();
    }

    [Fact]
    public async Task ConcurrentDiscovery_MultipleProviders_SameSerf()
    {
        // Arrange
        var config = CreateSerfConfig("node1", 17952, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        });

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        var provider1 = new NSerfServiceProvider(serf);
        var provider2 = new NSerfServiceProvider(serf);
        _providers.Add(provider1);
        _providers.Add(provider2);

        // Act
        await Task.WhenAll(
            provider1.StartAsync(),
            provider2.StartAsync()
        );

        await Task.Delay(500);

        var services1 = await provider1.DiscoverServicesAsync();
        var services2 = await provider2.DiscoverServicesAsync();

        // Assert - Both providers should discover the same service
        Assert.Single(services1);
        Assert.Single(services2);
        Assert.Equal(services1[0].Name, services2[0].Name);
    }

    [Fact]
    public async Task LargeMetadata_HandledCorrectly()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        };

        // Add many metadata tags
        for (int i = 0; i < 50; i++)
        {
            tags[$"meta_{i}"] = $"value_{i}";
        }

        var config = CreateSerfConfig("node1", 17953, tags);

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        var provider = new NSerfServiceProvider(serf);
        _providers.Add(provider);

        // Act
        await provider.StartAsync();
        await Task.Delay(500);

        var services = await provider.DiscoverServicesAsync();

        // Assert
        Assert.Single(services);
        var instance = services[0].Instances[0];

        // Verify metadata is preserved
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal($"value_{i}", instance.Metadata[$"meta_{i}"]);
        }
    }

    [Fact]
    public async Task SpecialCharactersInServiceName_HandledCorrectly()
    {
        // Arrange
        var config = CreateSerfConfig("node1", 17954, new Dictionary<string, string>
        {
            ["service:api-v2"] = "true",
            ["port:api-v2"] = "8080",
            ["service:metrics_collector"] = "true",
            ["port:metrics_collector"] = "9090"
        });

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        var provider = new NSerfServiceProvider(serf);
        _providers.Add(provider);

        // Act
        await provider.StartAsync();
        await Task.Delay(500);

        var services = await provider.DiscoverServicesAsync();

        // Assert
        Assert.Equal(2, services.Count);
        Assert.Contains(services, s => s.Name == "api-v2");
        Assert.Contains(services, s => s.Name == "metrics_collector");
    }

    [Fact]
    public async Task EdgePortNumbers_HandledCorrectly()
    {
        // Arrange
        var config = CreateSerfConfig("node1", 17955, new Dictionary<string, string>
        {
            ["service:min"] = "true",
            ["port:min"] = "1",
            ["service:max"] = "true",
            ["port:max"] = "65535",
            ["service:common"] = "true",
            ["port:common"] = "8080"
        });

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        var provider = new NSerfServiceProvider(serf);
        _providers.Add(provider);

        // Act
        await provider.StartAsync();
        await Task.Delay(500);

        var services = await provider.DiscoverServicesAsync();

        // Assert
        Assert.Equal(3, services.Count);

        var minService = services.First(s => s.Name == "min");
        Assert.Equal(1, minService.Instances[0].Port);

        var maxService = services.First(s => s.Name == "max");
        Assert.Equal(65535, maxService.Instances[0].Port);

        var commonService = services.First(s => s.Name == "common");
        Assert.Equal(8080, commonService.Instances[0].Port);
    }

    #region Helper Methods

    private Config CreateSerfConfig(string nodeName, int port, Dictionary<string, string> tags)
    {
        var memberlistConfig = NSerf.Memberlist.Configuration.MemberlistConfig.DefaultLANConfig();
        memberlistConfig.Name = nodeName;
        memberlistConfig.BindAddr = "127.0.0.1";
        memberlistConfig.BindPort = port;
        memberlistConfig.AdvertiseAddr = "127.0.0.1";
        memberlistConfig.AdvertisePort = port;

        return new Config
        {
            NodeName = nodeName,
            MemberlistConfig = memberlistConfig,
            Tags = tags,
            EventBuffer = 64
        };
    }

    public void Dispose()
    {
        // Cleanup providers
        foreach (var provider in _providers)
        {
            try
            {
                provider.StopAsync().GetAwaiter().GetResult();
                provider.Dispose();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error disposing provider: {ex.Message}");
            }
        }

        // Cleanup Serf instances
        foreach (var serf in _serfInstances)
        {
            try
            {
                serf.ShutdownAsync().GetAwaiter().GetResult();
                serf.Dispose();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error disposing Serf: {ex.Message}");
            }
        }

        _providers.Clear();
        _serfInstances.Clear();
    }

    #endregion
}
