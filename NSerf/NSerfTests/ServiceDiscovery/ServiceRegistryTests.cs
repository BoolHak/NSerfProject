using NSerf.ServiceDiscovery;
using Xunit;

namespace NSerfTests.ServiceDiscovery;

public class ServiceRegistryTests
{
    [Fact]
    public void RegisterInstanceAsync_NewInstance_RaisesRegisteredEvent()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        ServiceChangedEventArgs? capturedEvent = null;
        registry.ServiceChanged += (s, e) => capturedEvent = e;

        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080
        };

        // Act
        registry.RegisterInstanceAsync(instance).Wait();

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(ServiceChangeType.InstanceRegistered, capturedEvent.ChangeType);
        Assert.Equal("test-service", capturedEvent.ServiceName);
        Assert.Equal(instance, capturedEvent.Instance);
    }

    [Fact]
    public void RegisterInstanceAsync_ExistingInstance_RaisesUpdatedEvent()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance1 = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080,
            HealthStatus = InstanceHealthStatus.Unknown
        };

        registry.RegisterInstanceAsync(instance1).Wait();

        ServiceChangedEventArgs? capturedEvent = null;
        registry.ServiceChanged += (s, e) => capturedEvent = e;

        var instance2 = instance1 with { HealthStatus = InstanceHealthStatus.Healthy };

        // Act
        registry.RegisterInstanceAsync(instance2).Wait();

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(ServiceChangeType.InstanceUpdated, capturedEvent.ChangeType);
        Assert.Equal("test-service", capturedEvent.ServiceName);
        Assert.Equal(InstanceHealthStatus.Healthy, capturedEvent.Instance!.HealthStatus);
    }

    [Fact]
    public void DeregisterInstanceAsync_ExistingInstance_RaisesDeregisteredEvent()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080
        };

        registry.RegisterInstanceAsync(instance).Wait();

        ServiceChangedEventArgs? capturedEvent = null;
        registry.ServiceChanged += (s, e) => capturedEvent = e;

        // Act
        registry.DeregisterInstanceAsync("test-service", "instance1").Wait();

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(ServiceChangeType.InstanceDeregistered, capturedEvent.ChangeType);
        Assert.Equal("test-service", capturedEvent.ServiceName);
        Assert.Equal(instance.Id, capturedEvent.Instance!.Id);
    }

    [Fact]
    public void DeregisterInstanceAsync_NonExistentInstance_DoesNotRaiseEvent()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var eventRaised = false;
        registry.ServiceChanged += (s, e) => eventRaised = true;

        // Act
        registry.DeregisterInstanceAsync("test-service", "nonexistent").Wait();

        // Assert
        Assert.False(eventRaised);
    }

    [Fact]
    public void DeregisterInstanceAsync_LastInstance_RemovesService()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080
        };

        registry.RegisterInstanceAsync(instance).Wait();

        // Act
        registry.DeregisterInstanceAsync("test-service", "instance1").Wait();

        // Assert
        var service = registry.GetService("test-service");
        Assert.Null(service);
    }

    [Fact]
    public void UpdateHealthStatusAsync_ExistingInstance_UpdatesStatusAndRaisesEvent()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080,
            HealthStatus = InstanceHealthStatus.Unknown
        };

        registry.RegisterInstanceAsync(instance).Wait();

        ServiceChangedEventArgs? capturedEvent = null;
        registry.ServiceChanged += (s, e) => capturedEvent = e;

        // Act
        registry.UpdateHealthStatusAsync("test-service", "instance1", InstanceHealthStatus.Healthy).Wait();

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(ServiceChangeType.HealthStatusChanged, capturedEvent.ChangeType);
        Assert.Equal(InstanceHealthStatus.Healthy, capturedEvent.Instance!.HealthStatus);

        var instances = registry.GetInstances("test-service");
        Assert.Single(instances);
        Assert.Equal(InstanceHealthStatus.Healthy, instances[0].HealthStatus);
    }

    [Fact]
    public void UpdateHealthStatusAsync_NonExistentInstance_DoesNotRaiseEvent()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var eventRaised = false;
        registry.ServiceChanged += (s, e) => eventRaised = true;

        // Act
        registry.UpdateHealthStatusAsync("test-service", "nonexistent", InstanceHealthStatus.Healthy).Wait();

        // Assert
        Assert.False(eventRaised);
    }

    [Fact]
    public void GetServices_EmptyRegistry_ReturnsEmptyList()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act
        var services = registry.GetServices();

        // Assert
        Assert.Empty(services);
    }

    [Fact]
    public void GetServices_MultipleServices_ReturnsAllServices()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        
        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "service1",
            Host = "localhost",
            Port = 8080
        }).Wait();

        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance2",
            ServiceName = "service2",
            Host = "localhost",
            Port = 8081
        }).Wait();

        // Act
        var services = registry.GetServices();

        // Assert
        Assert.Equal(2, services.Count);
        Assert.Contains(services, s => s.Name == "service1");
        Assert.Contains(services, s => s.Name == "service2");
    }

    [Fact]
    public void GetService_ExistingService_ReturnsService()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080
        }).Wait();

        // Act
        var service = registry.GetService("test-service");

        // Assert
        Assert.NotNull(service);
        Assert.Equal("test-service", service.Name);
        Assert.Single(service.Instances);
    }

    [Fact]
    public void GetService_NonExistentService_ReturnsNull()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act
        var service = registry.GetService("nonexistent");

        // Assert
        Assert.Null(service);
    }

    [Fact]
    public void GetInstances_ExistingService_ReturnsAllInstances()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        
        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "host1",
            Port = 8080
        }).Wait();

        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance2",
            ServiceName = "test-service",
            Host = "host2",
            Port = 8080
        }).Wait();

        // Act
        var instances = registry.GetInstances("test-service");

        // Assert
        Assert.Equal(2, instances.Count);
    }

    [Fact]
    public void GetInstances_NonExistentService_ReturnsEmptyList()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act
        var instances = registry.GetInstances("nonexistent");

        // Assert
        Assert.Empty(instances);
    }

    [Fact]
    public void GetHealthyInstances_MixedHealthStatuses_ReturnsOnlyHealthy()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        
        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "host1",
            Port = 8080,
            HealthStatus = InstanceHealthStatus.Healthy
        }).Wait();

        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance2",
            ServiceName = "test-service",
            Host = "host2",
            Port = 8080,
            HealthStatus = InstanceHealthStatus.Unhealthy
        }).Wait();

        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance3",
            ServiceName = "test-service",
            Host = "host3",
            Port = 8080,
            HealthStatus = InstanceHealthStatus.Healthy
        }).Wait();

        // Act
        var healthyInstances = registry.GetHealthyInstances("test-service");

        // Assert
        Assert.Equal(2, healthyInstances.Count);
        Assert.All(healthyInstances, i => Assert.Equal(InstanceHealthStatus.Healthy, i.HealthStatus));
    }

    [Fact]
    public void GetHealthyInstances_NoHealthyInstances_ReturnsEmptyList()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        
        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "host1",
            Port = 8080,
            HealthStatus = InstanceHealthStatus.Unhealthy
        }).Wait();

        // Act
        var healthyInstances = registry.GetHealthyInstances("test-service");

        // Assert
        Assert.Empty(healthyInstances);
    }

    [Fact]
    public void ServiceRegistry_ConcurrentReads_DoNotBlock()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        
        for (int i = 0; i < 10; i++)
        {
            registry.RegisterInstanceAsync(new ServiceInstance
            {
                Id = $"instance{i}",
                ServiceName = "test-service",
                Host = "localhost",
                Port = 8080 + i
            }).Wait();
        }

        // Act - Multiple concurrent reads
        var tasks = new Task<IReadOnlyList<ServiceInstance>>[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = Task.Run(() => registry.GetInstances("test-service"));
        }

        Task.WaitAll(tasks);

        // Assert - All reads should succeed
        Assert.All(tasks, t => Assert.Equal(10, t.Result.Count));
    }

    [Fact]
    public void ServiceRegistry_ConcurrentWrites_AreThreadSafe()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act - Multiple concurrent writes
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() => registry.RegisterInstanceAsync(new ServiceInstance
            {
                Id = $"instance{index}",
                ServiceName = "test-service",
                Host = "localhost",
                Port = 8080
            }));
        }

        Task.WaitAll(tasks);

        // Assert - All instances should be registered
        var instances = registry.GetInstances("test-service");
        Assert.Equal(100, instances.Count);
    }

    [Fact]
    public void Service_Properties_CalculateCorrectly()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        
        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "host1",
            Port = 8080,
            HealthStatus = InstanceHealthStatus.Healthy
        }).Wait();

        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance2",
            ServiceName = "test-service",
            Host = "host2",
            Port = 8080,
            HealthStatus = InstanceHealthStatus.Unhealthy
        }).Wait();

        // Act
        var service = registry.GetService("test-service");

        // Assert
        Assert.NotNull(service);
        Assert.Equal(2, service.InstanceCount);
        Assert.Equal(1, service.HealthyInstanceCount);
        Assert.True(service.HasHealthyInstances);
    }

    [Fact]
    public void Dispose_DisposesLock()
    {
        // Arrange
        var registry = new ServiceRegistry();
        
        registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080
        }).Wait();

        // Act
        registry.Dispose();

        // Assert - Should not throw (lock is disposed)
        // Note: Accessing registry after disposal is undefined behavior,
        // but Dispose should complete without error
    }
}
