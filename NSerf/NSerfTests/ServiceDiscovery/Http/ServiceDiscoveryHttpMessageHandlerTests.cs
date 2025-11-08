using Microsoft.Extensions.Logging;
using NSerf.ServiceDiscovery;
using NSerf.ServiceDiscovery.Http;
using System.Net;
using Xunit;

namespace NSerfTests.ServiceDiscovery.Http;

/// <summary>
/// Unit tests for ServiceDiscoveryHttpMessageHandler.
/// </summary>
public sealed class ServiceDiscoveryHttpMessageHandlerTests : IDisposable
{
    private readonly ServiceRegistry _registry;
    private readonly TestHttpMessageHandler _innerHandler;
    private readonly ServiceDiscoveryHttpMessageHandler _handler;

    public ServiceDiscoveryHttpMessageHandlerTests()
    {
        _registry = new ServiceRegistry();
        _innerHandler = new TestHttpMessageHandler();
        _handler = new ServiceDiscoveryHttpMessageHandler(_registry)
        {
            InnerHandler = _innerHandler
        };
    }

    public void Dispose()
    {
        _handler.Dispose();
        _innerHandler.Dispose();
    }

    [Fact]
    public async Task SendAsync_ServiceNameResolved_RewritesUri()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api",
            Host = "10.0.1.5",
            Port = 8080,
            Scheme = "http",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = new HttpClient(_handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api/users/123");

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Equal("10.0.1.5", _innerHandler.LastRequest.RequestUri!.Host);
        Assert.Equal(8080, _innerHandler.LastRequest.RequestUri.Port);
        Assert.Equal("/users/123", _innerHandler.LastRequest.RequestUri.PathAndQuery);
    }

    [Fact]
    public async Task SendAsync_NoHealthyInstances_FallsBackToOriginalUri()
    {
        // Arrange
        var client = new HttpClient(_handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://unknown-service/test");

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Equal("unknown-service", _innerHandler.LastRequest.RequestUri!.Host);
    }

    [Fact]
    public async Task SendAsync_FailOnNoEndpoints_ThrowsException()
    {
        // Arrange
        var handler = new ServiceDiscoveryHttpMessageHandler(
            _registry,
            new ServiceDiscoveryHttpOptions { FailOnNoEndpoints = true })
        {
            InnerHandler = _innerHandler
        };

        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://unknown-service/test");

        // Act & Assert
        await Assert.ThrowsAsync<ServiceDiscoveryException>(
            async () => await client.SendAsync(request));

        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_IpAddress_DoesNotResolve()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "10.0.1.5",
            Host = "10.0.1.100",
            Port = 9000,
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = new HttpClient(_handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://10.0.1.5:8080/test");

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Equal("10.0.1.5", _innerHandler.LastRequest.RequestUri!.Host);
        Assert.Equal(8080, _innerHandler.LastRequest.RequestUri.Port);
    }

    [Fact]
    public async Task SendAsync_Localhost_DoesNotResolve()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "localhost",
            Host = "10.0.1.5",
            Port = 9000,
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = new HttpClient(_handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost:8080/test");

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Equal("localhost", _innerHandler.LastRequest.RequestUri!.Host);
        Assert.Equal(8080, _innerHandler.LastRequest.RequestUri.Port);
    }

    [Fact]
    public async Task SendAsync_Fqdn_DoesNotResolveByDefault()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api.example.com",
            Host = "10.0.1.5",
            Port = 9000,
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = new HttpClient(_handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.example.com/test");

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Equal("api.example.com", _innerHandler.LastRequest.RequestUri!.Host);
    }

    [Fact]
    public async Task SendAsync_FqdnWithResolveFqdnsEnabled_Resolves()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api.example.com",
            Host = "10.0.1.5",
            Port = 9000,
            Scheme = "http",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var handler = new ServiceDiscoveryHttpMessageHandler(
            _registry,
            new ServiceDiscoveryHttpOptions { ResolveFqdns = true })
        {
            InnerHandler = _innerHandler
        };

        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.example.com/test");

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Equal("10.0.1.5", _innerHandler.LastRequest.RequestUri!.Host);
        Assert.Equal(9000, _innerHandler.LastRequest.RequestUri.Port);

        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_NullRequestUri_PassesThrough()
    {
        // Arrange
        var client = new HttpClient(_handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        // RequestUri will be set to BaseAddress when null
    }

    [Fact]
    public async Task SendAsync_PreservesScheme_WhenInstanceHasScheme()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api",
            Host = "10.0.1.5",
            Port = 8443,
            Scheme = "https",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = new HttpClient(_handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api/test");

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Equal("https", _innerHandler.LastRequest.RequestUri!.Scheme);
        Assert.Equal(8443, _innerHandler.LastRequest.RequestUri.Port);
    }

    [Fact]
    public async Task SendAsync_PreservesQueryString()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api",
            Host = "10.0.1.5",
            Port = 8080,
            Scheme = "http",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = new HttpClient(_handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api/users?page=1&limit=10");

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Equal("/users?page=1&limit=10", _innerHandler.LastRequest.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task SendAsync_PreservesFragment()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api",
            Host = "10.0.1.5",
            Port = 8080,
            Scheme = "http",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = new HttpClient(_handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api/docs#section1");

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Equal("#section1", _innerHandler.LastRequest.RequestUri!.Fragment);
    }

    [Fact]
    public async Task SendAsync_OnlyHealthyInstances_AreUsed()
    {
        // Arrange
        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-1",
            ServiceName = "api",
            Host = "10.0.1.5",
            Port = 8080,
            HealthStatus = InstanceHealthStatus.Unhealthy
        });

        await _registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "api-2",
            ServiceName = "api",
            Host = "10.0.1.6",
            Port = 8080,
            Scheme = "http",
            HealthStatus = InstanceHealthStatus.Healthy
        });

        var client = new HttpClient(_handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api/test");

        // Act
        await client.SendAsync(request);

        // Assert
        Assert.NotNull(_innerHandler.LastRequest);
        Assert.Equal("10.0.1.6", _innerHandler.LastRequest.RequestUri!.Host);
    }

    [Fact]
    public void Constructor_NullRegistry_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceDiscoveryHttpMessageHandler(null!));
    }
}

/// <summary>
/// Test HTTP message handler that captures the last request.
/// </summary>
internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });
    }
}
