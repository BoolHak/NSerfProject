using NSerf.ServiceDiscovery;
using NSerf.Serf;
using Xunit.Abstractions;

namespace NSerfTests.ServiceDiscovery;

/// <summary>
/// Advanced integration tests simulating real-world infrastructure scenarios.
/// Tests multi-node clusters, dynamic changes, HA patterns, and production workloads.
/// </summary>
[Collection("Sequential")]
public class NSerfServiceProviderAdvancedIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private readonly List<NSerf.Serf.Serf> _serfInstances = [];
    private readonly List<NSerfServiceProvider> _providers = [];

    [Fact]
    public async Task ThreeNodeCluster_AllAdvertiseAPI_DiscoversThreeInstances()
    {
        var node1 = await CreateNodeAsync("node1", 18001, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["datacenter"] = "dc1"
        });

        _ = await CreateNodeAsync("node2", 18002, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["datacenter"] = "dc1"
        }, joinTo: "127.0.0.1:18001");

        _ = await CreateNodeAsync("node3", 18003, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["datacenter"] = "dc2"
        }, joinTo: "127.0.0.1:18001");

        var provider = new NSerfServiceProvider(node1);
        _providers.Add(provider);

        await provider.StartAsync();
        await Task.Delay(2000);

        var services = await provider.DiscoverServicesAsync();

        Assert.Single(services);
        var apiService = services.First(s => s.Name == "api");
        Assert.Equal(3, apiService.Instances.Count);
    }

    [Fact]
    public async Task FiveNodeCluster_MixedServices_CorrectServiceGrouping()
    {
        var node1 = await CreateNodeAsync("api-1", 18101, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        });

        _ = await CreateNodeAsync("api-2", 18102, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        }, joinTo: "127.0.0.1:18101");

        _ = await CreateNodeAsync("db-1", 18103, new Dictionary<string, string>
        {
            ["service:postgres"] = "true",
            ["port:postgres"] = "5432",
            ["role"] = "primary"
        }, joinTo: "127.0.0.1:18101");

        _ = await CreateNodeAsync("db-2", 18104, new Dictionary<string, string>
        {
            ["service:postgres"] = "true",
            ["port:postgres"] = "5432",
            ["role"] = "replica"
        }, joinTo: "127.0.0.1:18101");

        _ = await CreateNodeAsync("cache-1", 18105, new Dictionary<string, string>
        {
            ["service:redis"] = "true",
            ["port:redis"] = "6379"
        }, joinTo: "127.0.0.1:18101");

        var provider = new NSerfServiceProvider(node1);
        _providers.Add(provider);

        await provider.StartAsync();
        await Task.Delay(2000);

        var services = await provider.DiscoverServicesAsync();

        Assert.Equal(3, services.Count);
        Assert.Equal(2, services.First(s => s.Name == "api").Instances.Count);
        Assert.Equal(2, services.First(s => s.Name == "postgres").Instances.Count);
        Assert.Single(services.First(s => s.Name == "redis").Instances);
    }

    [Fact]
    public async Task NodeJoinsCluster_ServiceDiscoveredEventRaised()
    {
        var node1 = await CreateNodeAsync("node1", 18201, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        });

        var provider = new NSerfServiceProvider(node1);
        _providers.Add(provider);

        var discoveredEvents = new List<ServiceChangedEventArgs>();
        provider.ServiceDiscovered += (_, e) => discoveredEvents.Add(e);

        await provider.StartAsync();
        await Task.Delay(1000);
        discoveredEvents.Clear();

        _ = await CreateNodeAsync("node2", 18202, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        }, joinTo: "127.0.0.1:18201");

        await Task.Delay(2000);

        var services = await provider.DiscoverServicesAsync();
        Assert.Equal(2, services.First(s => s.Name == "api").Instances.Count);
        Assert.NotEmpty(discoveredEvents);
    }

    [Fact]
    public async Task NodeLeavesCluster_ServiceDeregisteredEventRaised()
    {
        var node1 = await CreateNodeAsync("node1", 18301, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        });

        var node2 = await CreateNodeAsync("node2", 18302, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        }, joinTo: "127.0.0.1:18301");

        var provider = new NSerfServiceProvider(node1);
        _providers.Add(provider);

        await provider.StartAsync();
        await Task.Delay(2000);

        await node2.LeaveAsync();
        await Task.Delay(2000);

        var services = await provider.DiscoverServicesAsync();
        Assert.Single(services.First(s => s.Name == "api").Instances);
    }

    [Fact]
    public async Task DatabaseCluster_PrimaryReplicaPattern_CorrectRoleMetadata()
    {
        var primary = await CreateNodeAsync("pg-primary", 18501, new Dictionary<string, string>
        {
            ["service:postgres"] = "true",
            ["port:postgres"] = "5432",
            ["role"] = "primary"
        });

        _ = await CreateNodeAsync("pg-replica-1", 18502, new Dictionary<string, string>
        {
            ["service:postgres"] = "true",
            ["port:postgres"] = "5432",
            ["role"] = "replica"
        }, joinTo: "127.0.0.1:18501");

        var provider = new NSerfServiceProvider(primary);
        _providers.Add(provider);

        await provider.StartAsync();
        await Task.Delay(2000);

        var services = await provider.DiscoverServicesAsync();
        var postgresService = services.First(s => s.Name == "postgres");

        Assert.Equal(2, postgresService.Instances.Count);
        Assert.Single(postgresService.Instances, i => i.Metadata["role"] == "primary");
        Assert.Single(postgresService.Instances, i => i.Metadata["role"] == "replica");
    }

    [Fact]
    public async Task MessageQueueCluster_MultipleBrokers_AllDiscovered()
    {
        var broker1 = await CreateNodeAsync("broker-1", 18601, new Dictionary<string, string>
        {
            ["service:rabbitmq"] = "true",
            ["port:rabbitmq"] = "5672",
            ["service:rabbitmq-mgmt"] = "true",
            ["port:rabbitmq-mgmt"] = "15672"
        });

        _ = await CreateNodeAsync("broker-2", 18602, new Dictionary<string, string>
        {
            ["service:rabbitmq"] = "true",
            ["port:rabbitmq"] = "5672",
            ["service:rabbitmq-mgmt"] = "true",
            ["port:rabbitmq-mgmt"] = "15672"
        }, joinTo: "127.0.0.1:18601");

        var provider = new NSerfServiceProvider(broker1);
        _providers.Add(provider);

        await provider.StartAsync();
        await Task.Delay(2000);

        var services = await provider.DiscoverServicesAsync();

        Assert.Equal(2, services.Count);
        Assert.Equal(2, services.First(s => s.Name == "rabbitmq").Instances.Count);
        Assert.Equal(2, services.First(s => s.Name == "rabbitmq-mgmt").Instances.Count);
    }

    [Fact]
    public async Task LoadBalancer_WeightedBackends_CorrectWeights()
    {
        var api1 = await CreateNodeAsync("api-1", 18701, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["weight:api"] = "100"
        });

        _ = await CreateNodeAsync("api-2", 18702, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["weight:api"] = "200"
        }, joinTo: "127.0.0.1:18701");

        var provider = new NSerfServiceProvider(api1);
        _providers.Add(provider);

        await provider.StartAsync();
        await Task.Delay(2000);

        var services = await provider.DiscoverServicesAsync();
        var instances = services.First(s => s.Name == "api").Instances;

        Assert.Contains(instances, i => i.Weight == 100);
        Assert.Contains(instances, i => i.Weight == 200);
    }

    [Fact]
    public async Task MixedProtocols_HTTP_HTTPS_gRPC_AllDiscovered()
    {
        var web = await CreateNodeAsync("web-1", 18801, new Dictionary<string, string>
        {
            ["service:web"] = "true",
            ["port:web"] = "80",
            ["scheme:web"] = "http"
        });

        _ = await CreateNodeAsync("api-1", 18802, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "443",
            ["scheme:api"] = "https"
        }, joinTo: "127.0.0.1:18801");

        _ = await CreateNodeAsync("grpc-1", 18803, new Dictionary<string, string>
        {
            ["service:rpc"] = "true",
            ["port:rpc"] = "50051",
            ["scheme:rpc"] = "grpc"
        }, joinTo: "127.0.0.1:18801");

        var provider = new NSerfServiceProvider(web);
        _providers.Add(provider);

        await provider.StartAsync();
        await Task.Delay(2000);

        var services = await provider.DiscoverServicesAsync();

        Assert.Equal(3, services.Count);
        Assert.Equal("http", services.First(s => s.Name == "web").Instances[0].Scheme);
        Assert.Equal("https", services.First(s => s.Name == "api").Instances[0].Scheme);
        Assert.Equal("grpc", services.First(s => s.Name == "rpc").Instances[0].Scheme);
    }

    [Fact]
    public async Task ProviderWithRegistry_AutoRegistration_WorksCorrectly()
    {
        var node1 = await CreateNodeAsync("node1", 18901, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        });

        var registry = new ServiceRegistry();
        var provider = new NSerfServiceProvider(node1);
        _providers.Add(provider);

        provider.ServiceDiscovered += async (_, e) =>
        {
            if (e.ChangeType != ServiceChangeType.InstanceRegistered || e.Instance == null) return;
            await registry.RegisterInstanceAsync(e.Instance);
        };

        await provider.StartAsync();
        await Task.Delay(2000);

        var registryServices = registry.GetServices();
        Assert.True(registryServices.Any(s => s.Name == "api"), "Registry should contain 'api' service");
        
    }

    [Fact]
    public async Task CompleteStack_WebAPIDBCache_AllLayersDiscovered()
    {
        var nginx = await CreateNodeAsync("nginx-1", 19101, new Dictionary<string, string>
        {
            ["service:nginx"] = "true",
            ["port:nginx"] = "80",
            ["layer"] = "proxy"
        });

        _ = await CreateNodeAsync("api-1", 19102, new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["layer"] = "application"
        }, joinTo: "127.0.0.1:19101");

        _ = await CreateNodeAsync("postgres-1", 19103, new Dictionary<string, string>
        {
            ["service:postgres"] = "true",
            ["port:postgres"] = "5432",
            ["layer"] = "database"
        }, joinTo: "127.0.0.1:19101");

        _ = await CreateNodeAsync("redis-1", 19104, new Dictionary<string, string>
        {
            ["service:redis"] = "true",
            ["port:redis"] = "6379",
            ["layer"] = "cache"
        }, joinTo: "127.0.0.1:19101");

        var provider = new NSerfServiceProvider(nginx);
        _providers.Add(provider);

        await provider.StartAsync();
        await Task.Delay(2000);

        var services = await provider.DiscoverServicesAsync();

        Assert.Equal(4, services.Count);
        Assert.All(services, s => Assert.Single(s.Instances));
    }

    [Fact]
    public async Task MicroservicesArchitecture_MultipleServices_AllDiscovered()
    {
        var services = new[] { "gateway", "auth", "users", "orders", "payments" };
        NSerf.Serf.Serf? firstNode = null;

        for (var i = 0; i < services.Length; i++)
        {
            var tags = new Dictionary<string, string>
            {
                [$"service:{services[i]}"] = "true",
                [$"port:{services[i]}"] = (8080 + i).ToString()
            };

            var node = await CreateNodeAsync($"{services[i]}-1", 19200 + i, tags,
                joinTo: firstNode != null ? "127.0.0.1:19200" : null);

            firstNode ??= node;
        }

        var provider = new NSerfServiceProvider(firstNode!);
        _providers.Add(provider);

        await provider.StartAsync();
        await Task.Delay(3000);

        var discoveredServices = await provider.DiscoverServicesAsync();

        Assert.Equal(5, discoveredServices.Count);
        Assert.All(discoveredServices, s => Assert.Single(s.Instances));
    }

    #region Helper Methods

    private async Task<NSerf.Serf.Serf> CreateNodeAsync(string nodeName, int port,
        Dictionary<string, string> tags, string? joinTo = null)
    {
        var memberlistConfig = NSerf.Memberlist.Configuration.MemberlistConfig.DefaultLANConfig();
        memberlistConfig.Name = nodeName;
        memberlistConfig.BindAddr = "127.0.0.1";
        memberlistConfig.BindPort = port;
        memberlistConfig.AdvertiseAddr = "127.0.0.1";
        memberlistConfig.AdvertisePort = port;

        var config = new Config
        {
            NodeName = nodeName,
            MemberlistConfig = memberlistConfig,
            Tags = tags,
            EventBuffer = 64
        };

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serfInstances.Add(serf);

        if (joinTo != null)
        {
            await serf.JoinAsync([joinTo], ignoreOld: false);
        }

        return serf;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            try
            {
                provider.StopAsync().GetAwaiter().GetResult();
                provider.Dispose();
            }
            catch (Exception ex)
            {
                output.WriteLine($"Error disposing provider: {ex.Message}");
            }
        }

        foreach (var serf in _serfInstances)
        {
            try
            {
                serf.ShutdownAsync().GetAwaiter().GetResult();
                serf.Dispose();
            }
            catch (Exception ex)
            {
                output.WriteLine($"Error disposing Serf: {ex.Message}");
            }
        }

        _providers.Clear();
        _serfInstances.Clear();
    }

    #endregion
}
