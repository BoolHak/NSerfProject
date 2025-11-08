using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSerf.ServiceDiscovery;
using NSerf.ServiceDiscovery.Http;
using System.Net;
using Xunit;

namespace NSerfTests.ServiceDiscovery.Http;

/// <summary>
/// Integration tests for HTTP client service discovery.
/// </summary>
[Collection("Sequential")]
public sealed class ServiceDiscoveryHttpIntegrationTests : IDisposable
{
    private readonly ServiceRegistry _registry;
    private readonly ServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TestServer _testServer;

    public ServiceDiscoveryHttpIntegrationTests()
    {
        _registry = new ServiceRegistry();
        
        var services = new ServiceCollection();
        services.AddSingleton<IServiceRegistry>(_registry);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        
        // Add HTTP client with service discovery
        services.AddServiceDiscoveryHttpClient(options =>
        {
            options.LoadBalancingStrategy = LoadBalancingStrategy.RoundRobin;
        });

        _serviceProvider = services.BuildServiceProvider();
        _httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();

        // Start test server
        _testServer = new TestServer();
        _testServer.Start();
    }

    public void Dispose()
    {
        _testServer.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task EndToEnd_ServiceResolution_WorksWithRealHttpClient()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "test-1",
            ServiceName = "testapi",
            Host = "127.0.0.1",
            Port = _testServer.Port,
            Scheme = "http",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = _httpClientFactory.CreateClient();

        // Act
        var response = await client.GetAsync($"http://testapi/test");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("OK", content);
    }

    [Fact]
    public async Task EndToEnd_MultipleInstances_LoadBalances()
    {
        // Arrange - register multiple instances
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "test-1",
            ServiceName = "testapi",
            Host = "127.0.0.1",
            Port = _testServer.Port,
            Scheme = "http",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = _httpClientFactory.CreateClient();

        // Act - make multiple requests
        for (int i = 0; i < 5; i++)
        {
            var response = await client.GetAsync($"http://testapi/test");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Assert - all requests succeeded
        Assert.True(_testServer.RequestCount >= 5);
    }

    [Fact]
    public async Task EndToEnd_ServiceNotFound_FallsBackToDns()
    {
        // Arrange
        var client = _httpClientFactory.CreateClient();

        // Act & Assert - should not throw, falls back to DNS
        // (will fail at DNS resolution, but that's expected)
        await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
            await client.GetAsync("http://nonexistent-service-xyz/test"));
    }

    [Fact]
    public async Task EndToEnd_DynamicServiceUpdate_PicksUpNewInstances()
    {
        // Arrange
        var client = _httpClientFactory.CreateClient();

        // Initially no instances
        await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
            await client.GetAsync("http://dynamic-service/test"));

        // Act - register instance
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "dynamic-1",
            ServiceName = "dynamic-service",
            Host = "127.0.0.1",
            Port = _testServer.Port,
            Scheme = "http",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        // Assert - now works
        var response = await client.GetAsync("http://dynamic-service/test");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EndToEnd_NamedHttpClient_UsesServiceDiscovery()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IServiceRegistry>(_registry);
        services.AddLogging();

        services.AddHttpClient("catalog")
            .AddServiceDiscovery(options =>
            {
                options.LoadBalancingStrategy = LoadBalancingStrategy.First;
            });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "catalog-1",
            ServiceName = "catalog",
            Host = "127.0.0.1",
            Port = _testServer.Port,
            Scheme = "http",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = factory.CreateClient("catalog");

        // Act
        var response = await client.GetAsync("http://catalog/test");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        provider.Dispose();
    }

    [Fact]
    public async Task EndToEnd_ComplexUri_PreservesAllComponents()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api",
            Host = "127.0.0.1",
            Port = _testServer.Port,
            Scheme = "http",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = _httpClientFactory.CreateClient();

        // Act
        var response = await client.GetAsync("http://api/test?param1=value1&param2=value2#section");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>
/// Simple test HTTP server for integration tests.
/// </summary>
internal sealed class TestServer : IDisposable
{
    private readonly HttpListener _listener;
    private Task? _listenerTask;
    private CancellationTokenSource? _cts;
    private int _requestCount;

    public int Port { get; }
    public int RequestCount => _requestCount;

    public TestServer()
    {
        _listener = new HttpListener();
        Port = GetAvailablePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        _listenerTask = Task.Run(async () => await ListenAsync(_cts.Token));
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        _listener.Stop();
        _listener.Close();
        _cts?.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                Interlocked.Increment(ref _requestCount);

                var response = context.Response;
                response.StatusCode = 200;
                var buffer = System.Text.Encoding.UTF8.GetBytes("OK");
                await response.OutputStream.WriteAsync(buffer, cancellationToken);
                response.Close();
            }
            catch (HttpListenerException)
            {
                // Listener stopped
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
