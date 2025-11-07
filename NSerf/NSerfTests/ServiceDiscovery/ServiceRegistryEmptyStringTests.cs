using NSerf.ServiceDiscovery;
using Xunit;

namespace NSerfTests.ServiceDiscovery;

/// <summary>
/// Tests for empty string validation in ServiceRegistry
/// </summary>
public class ServiceRegistryEmptyStringTests
{
    [Fact]
    public void GetService_EmptyServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.GetService(""));
    }

    [Fact]
    public void GetService_WhitespaceServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.GetService("   "));
    }

    [Fact]
    public void GetInstances_EmptyServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.GetInstances(""));
    }

    [Fact]
    public void GetInstances_WhitespaceServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.GetInstances("   "));
    }

    [Fact]
    public void GetHealthyInstances_EmptyServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.GetHealthyInstances(""));
    }

    [Fact]
    public void GetHealthyInstances_WhitespaceServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.GetHealthyInstances("   "));
    }

    [Fact]
    public async Task RegisterInstanceAsync_EmptyServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "",
            Host = "localhost",
            Port = 8080
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.RegisterInstanceAsync(instance));
    }

    [Fact]
    public async Task RegisterInstanceAsync_WhitespaceServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "   ",
            Host = "localhost",
            Port = 8080
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.RegisterInstanceAsync(instance));
    }

    [Fact]
    public async Task RegisterInstanceAsync_EmptyInstanceId_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance = new ServiceInstance
        {
            Id = "",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.RegisterInstanceAsync(instance));
    }

    [Fact]
    public async Task RegisterInstanceAsync_WhitespaceInstanceId_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance = new ServiceInstance
        {
            Id = "   ",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.RegisterInstanceAsync(instance));
    }

    [Fact]
    public async Task DeregisterInstanceAsync_EmptyServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.DeregisterInstanceAsync("", "instance1"));
    }

    [Fact]
    public async Task DeregisterInstanceAsync_WhitespaceServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.DeregisterInstanceAsync("   ", "instance1"));
    }

    [Fact]
    public async Task DeregisterInstanceAsync_EmptyInstanceId_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.DeregisterInstanceAsync("test-service", ""));
    }

    [Fact]
    public async Task DeregisterInstanceAsync_WhitespaceInstanceId_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.DeregisterInstanceAsync("test-service", "   "));
    }

    [Fact]
    public async Task UpdateHealthStatusAsync_EmptyServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.UpdateHealthStatusAsync("", "instance1", InstanceHealthStatus.Healthy));
    }

    [Fact]
    public async Task UpdateHealthStatusAsync_WhitespaceServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.UpdateHealthStatusAsync("   ", "instance1", InstanceHealthStatus.Healthy));
    }

    [Fact]
    public async Task UpdateHealthStatusAsync_EmptyInstanceId_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.UpdateHealthStatusAsync("test-service", "", InstanceHealthStatus.Healthy));
    }

    [Fact]
    public async Task UpdateHealthStatusAsync_WhitespaceInstanceId_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await registry.UpdateHealthStatusAsync("test-service", "   ", InstanceHealthStatus.Healthy));
    }
}
