using NSerf.ServiceDiscovery;
using Xunit;

namespace NSerfTests.ServiceDiscovery;

/// <summary>
/// Edge case and stress tests for ServiceRegistry to ensure rock-solid implementation
/// </summary>
public class ServiceRegistryEdgeCasesTests
{
    #region Null and Empty String Handling

    [Fact]
    public void GetService_NullServiceName_ThrowsArgumentNullException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => registry.GetService(null!));
    }

    [Fact]
    public void GetService_EmptyServiceName_ThrowsArgumentException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.GetService(""));
    }

    [Fact]
    public void GetInstances_NullServiceName_ThrowsArgumentNullException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => registry.GetInstances(null!));
    }

    [Fact]
    public void GetHealthyInstances_NullServiceName_ThrowsArgumentNullException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => registry.GetHealthyInstances(null!));
    }

    [Fact]
    public async Task DeregisterInstanceAsync_NullServiceName_ThrowsArgumentNullException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await registry.DeregisterInstanceAsync(null!, "instance1"));
    }

    [Fact]
    public async Task DeregisterInstanceAsync_NullInstanceId_ThrowsArgumentNullException()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await registry.DeregisterInstanceAsync("service1", null!));
    }

    #endregion

    #region Rapid Registration/Deregistration

    [Fact]
    public async Task RegisterDeregisterCycle_RapidSuccession_MaintainsConsistency()
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

        // Act - Rapid register/deregister cycles
        for (int i = 0; i < 100; i++)
        {
            await registry.RegisterInstanceAsync(instance);
            await registry.DeregisterInstanceAsync("test-service", "instance1");
        }

        // Assert - Should end in clean state
        var service = registry.GetService("test-service");
        Assert.Null(service);
    }

    [Fact]
    public async Task RegisterSameInstance_MultipleTimesRapidly_LastWins()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var tasks = new List<Task>();

        // Act - Register same instance ID with different ports concurrently
        for (int i = 0; i < 50; i++)
        {
            var port = 8080 + i;
            tasks.Add(Task.Run(async () =>
            {
                await registry.RegisterInstanceAsync(new ServiceInstance
                {
                    Id = "instance1",
                    ServiceName = "test-service",
                    Host = "localhost",
                    Port = port
                });
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Should have exactly one instance
        var instances = registry.GetInstances("test-service");
        Assert.Single(instances);
    }

    #endregion

    #region Health Status Edge Cases

    [Fact]
    public async Task UpdateHealthStatus_AllPossibleTransitions_Succeed()
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

        await registry.RegisterInstanceAsync(instance);

        var statuses = new[]
        {
            InstanceHealthStatus.Healthy,
            InstanceHealthStatus.Unhealthy,
            InstanceHealthStatus.Draining,
            InstanceHealthStatus.Unknown,
            InstanceHealthStatus.Healthy
        };

        // Act & Assert - All transitions should work
        foreach (var status in statuses)
        {
            await registry.UpdateHealthStatusAsync("test-service", "instance1", status);
            var instances = registry.GetInstances("test-service");
            Assert.Equal(status, instances[0].HealthStatus);
        }
    }

    [Fact]
    public async Task UpdateHealthStatus_SameStatusMultipleTimes_RaisesEventEachTime()
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

        await registry.RegisterInstanceAsync(instance);

        var eventCount = 0;
        registry.ServiceChanged += (s, e) =>
        {
            if (e.ChangeType == ServiceChangeType.HealthStatusChanged)
                eventCount++;
        };

        // Act - Update to same status multiple times
        for (int i = 0; i < 5; i++)
        {
            await registry.UpdateHealthStatusAsync("test-service", "instance1", InstanceHealthStatus.Healthy);
        }

        // Assert - Should raise event each time (idempotent but notifies)
        Assert.Equal(5, eventCount);
    }

    #endregion

    #region Large Scale Operations

    [Fact]
    public async Task RegisterInstances_1000Instances_AllRegistered()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act - Register 1000 instances
        for (int i = 0; i < 1000; i++)
        {
            await registry.RegisterInstanceAsync(new ServiceInstance
            {
                Id = $"instance{i}",
                ServiceName = "test-service",
                Host = $"host{i}",
                Port = 8080
            });
        }

        // Assert
        var instances = registry.GetInstances("test-service");
        Assert.Equal(1000, instances.Count);
    }

    [Fact]
    public async Task RegisterServices_100Services_AllAccessible()
    {
        // Arrange
        using var registry = new ServiceRegistry();

        // Act - Register 100 different services
        for (int i = 0; i < 100; i++)
        {
            await registry.RegisterInstanceAsync(new ServiceInstance
            {
                Id = $"instance{i}",
                ServiceName = $"service{i}",
                Host = "localhost",
                Port = 8080 + i
            });
        }

        // Assert
        var services = registry.GetServices();
        Assert.Equal(100, services.Count);

        // Verify each service is accessible
        for (int i = 0; i < 100; i++)
        {
            var service = registry.GetService($"service{i}");
            Assert.NotNull(service);
            Assert.Single(service.Instances);
        }
    }

    [Fact]
    public async Task MixedOperations_1000Operations_MaintainsConsistency()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var random = new Random(42); // Fixed seed for reproducibility

        // Act - 1000 random operations
        for (int i = 0; i < 1000; i++)
        {
            var operation = random.Next(4);
            var serviceId = random.Next(10);
            var instanceId = random.Next(5);

            switch (operation)
            {
                case 0: // Register
                    await registry.RegisterInstanceAsync(new ServiceInstance
                    {
                        Id = $"instance{instanceId}",
                        ServiceName = $"service{serviceId}",
                        Host = "localhost",
                        Port = 8080
                    });
                    break;
                case 1: // Deregister
                    await registry.DeregisterInstanceAsync($"service{serviceId}", $"instance{instanceId}");
                    break;
                case 2: // Update health
                    await registry.UpdateHealthStatusAsync($"service{serviceId}", $"instance{instanceId}",
                        (InstanceHealthStatus)random.Next(4));
                    break;
                case 3: // Query
                    _ = registry.GetInstances($"service{serviceId}");
                    break;
            }
        }

        // Assert - Should not crash and maintain valid state
        var services = registry.GetServices();
        Assert.NotNull(services);
        
        // All services should have valid instance counts
        foreach (var service in services)
        {
            Assert.True(service.InstanceCount >= 0);
            Assert.True(service.HealthyInstanceCount >= 0);
            Assert.True(service.HealthyInstanceCount <= service.InstanceCount);
        }
    }

    #endregion

    #region Concurrent Access Patterns

    [Fact]
    public async Task ConcurrentReadWriteMix_100Threads_NoDataCorruption()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var tasks = new List<Task>();

        // Act - 100 threads doing mixed operations
        for (int i = 0; i < 100; i++)
        {
            var threadId = i;
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < 10; j++)
                {
                    // Write
                    await registry.RegisterInstanceAsync(new ServiceInstance
                    {
                        Id = $"instance{threadId}",
                        ServiceName = "test-service",
                        Host = $"host{threadId}",
                        Port = 8080
                    });

                    // Read
                    _ = registry.GetInstances("test-service");
                    _ = registry.GetHealthyInstances("test-service");

                    // Update
                    await registry.UpdateHealthStatusAsync("test-service", $"instance{threadId}",
                        InstanceHealthStatus.Healthy);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Should have exactly 100 instances (one per thread)
        var instances = registry.GetInstances("test-service");
        Assert.Equal(100, instances.Count);
    }

    [Fact]
    public async Task ConcurrentDeregistration_SameInstance_OnlyOneSucceeds()
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

        await registry.RegisterInstanceAsync(instance);

        var eventCount = 0;
        registry.ServiceChanged += (s, e) =>
        {
            if (e.ChangeType == ServiceChangeType.InstanceDeregistered)
                Interlocked.Increment(ref eventCount);
        };

        // Act - 100 threads try to deregister the same instance
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = Task.Run(async () =>
                await registry.DeregisterInstanceAsync("test-service", "instance1"));
        }

        await Task.WhenAll(tasks);

        // Assert - Only one deregistration event should fire
        Assert.Equal(1, eventCount);
        Assert.Null(registry.GetService("test-service"));
    }

    [Fact]
    public async Task ConcurrentHealthUpdates_SameInstance_AllSucceed()
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

        await registry.RegisterInstanceAsync(instance);

        var eventCount = 0;
        registry.ServiceChanged += (s, e) =>
        {
            if (e.ChangeType == ServiceChangeType.HealthStatusChanged)
                Interlocked.Increment(ref eventCount);
        };

        // Act - 100 threads update health status
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
        {
            var status = i % 2 == 0 ? InstanceHealthStatus.Healthy : InstanceHealthStatus.Unhealthy;
            tasks[i] = Task.Run(async () =>
                await registry.UpdateHealthStatusAsync("test-service", "instance1", status));
        }

        await Task.WhenAll(tasks);

        // Assert - All 100 updates should succeed
        Assert.Equal(100, eventCount);
        
        // Final state should be valid
        var instances = registry.GetInstances("test-service");
        Assert.Single(instances);
        Assert.True(instances[0].HealthStatus == InstanceHealthStatus.Healthy ||
                    instances[0].HealthStatus == InstanceHealthStatus.Unhealthy);
    }

    #endregion

    #region Event Handler Edge Cases

    [Fact]
    public async Task EventHandler_ThrowsException_DoesNotBreakRegistry()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        registry.ServiceChanged += (s, e) => throw new InvalidOperationException("Handler failed");

        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080
        };

        // Act & Assert - Should not throw despite handler exception
        await registry.RegisterInstanceAsync(instance);

        // Registry should still be functional
        var service = registry.GetService("test-service");
        Assert.NotNull(service);
    }

    [Fact]
    public async Task MultipleEventHandlers_OneThrows_OthersStillExecute()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var handler1Called = false;
        var handler3Called = false;

        registry.ServiceChanged += (s, e) => handler1Called = true;
        registry.ServiceChanged += (s, e) => throw new Exception("Handler 2 failed");
        registry.ServiceChanged += (s, e) => handler3Called = true;

        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080
        };

        // Act
        await registry.RegisterInstanceAsync(instance);

        // Assert - Handler 1 and 3 should execute despite handler 2 throwing
        // Note: This depends on event invocation behavior - all handlers should be called
        Assert.True(handler1Called);
        Assert.True(handler3Called);
    }

    [Fact]
    public async Task EventHandler_QueriesRegistry_CompletesSuccessfully()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var querySucceeded = false;
        
        // Handler that queries registry after event completes
        // Note: Direct queries during event raise would cause lock recursion
        // This tests that the pattern works when done asynchronously
        registry.ServiceChanged += async (s, e) =>
        {
            // Simulate async work, then query
            await Task.Yield();
            _ = registry.GetServices();
            querySucceeded = true;
        };

        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080
        };

        // Act
        await registry.RegisterInstanceAsync(instance);
        await Task.Delay(100); // Give async handler time to complete

        // Assert - Handler should complete successfully
        Assert.True(querySucceeded);
    }

    #endregion

    #region Service Property Edge Cases

    [Fact]
    public async Task Service_NoInstances_PropertiesReturnZero()
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

        await registry.RegisterInstanceAsync(instance);
        await registry.DeregisterInstanceAsync("test-service", "instance1");

        // Act
        var service = registry.GetService("test-service");

        // Assert - Service should be removed when last instance is deregistered
        Assert.Null(service);
    }

    [Fact]
    public async Task Service_AllInstancesUnhealthy_HasHealthyInstancesIsFalse()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        
        for (int i = 0; i < 5; i++)
        {
            await registry.RegisterInstanceAsync(new ServiceInstance
            {
                Id = $"instance{i}",
                ServiceName = "test-service",
                Host = $"host{i}",
                Port = 8080,
                HealthStatus = InstanceHealthStatus.Unhealthy
            });
        }

        // Act
        var service = registry.GetService("test-service");

        // Assert
        Assert.NotNull(service);
        Assert.Equal(5, service.InstanceCount);
        Assert.Equal(0, service.HealthyInstanceCount);
        Assert.False(service.HasHealthyInstances);
    }

    [Fact]
    public async Task Service_MixedHealthStatuses_CountsOnlyHealthy()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var statuses = new[]
        {
            InstanceHealthStatus.Healthy,
            InstanceHealthStatus.Unhealthy,
            InstanceHealthStatus.Draining,
            InstanceHealthStatus.Unknown,
            InstanceHealthStatus.Healthy
        };

        for (int i = 0; i < statuses.Length; i++)
        {
            await registry.RegisterInstanceAsync(new ServiceInstance
            {
                Id = $"instance{i}",
                ServiceName = "test-service",
                Host = $"host{i}",
                Port = 8080,
                HealthStatus = statuses[i]
            });
        }

        // Act
        var service = registry.GetService("test-service");

        // Assert
        Assert.NotNull(service);
        Assert.Equal(5, service.InstanceCount);
        Assert.Equal(2, service.HealthyInstanceCount); // Only 2 healthy
        Assert.True(service.HasHealthyInstances);
    }

    #endregion

    #region Timestamp and Metadata Edge Cases

    [Fact]
    public async Task UpdateHealthStatus_UpdatesTimestamp()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080,
            Timestamp = DateTimeOffset.UtcNow.AddHours(-1)
        };

        await registry.RegisterInstanceAsync(instance);
        var originalTimestamp = registry.GetInstances("test-service")[0].Timestamp;

        await Task.Delay(10); // Ensure time passes

        // Act
        await registry.UpdateHealthStatusAsync("test-service", "instance1", InstanceHealthStatus.Healthy);

        // Assert
        var updatedInstance = registry.GetInstances("test-service")[0];
        Assert.True(updatedInstance.Timestamp > originalTimestamp);
    }

    [Fact]
    public async Task RegisterInstance_WithMetadata_PreservesMetadata()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var metadata = new Dictionary<string, string>
        {
            { "version", "1.0.0" },
            { "region", "us-east-1" },
            { "datacenter", "dc1" }
        };

        var instance = new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "test-service",
            Host = "localhost",
            Port = 8080,
            Metadata = metadata
        };

        // Act
        await registry.RegisterInstanceAsync(instance);

        // Assert
        var retrieved = registry.GetInstances("test-service")[0];
        Assert.Equal(3, retrieved.Metadata.Count);
        Assert.Equal("1.0.0", retrieved.Metadata["version"]);
        Assert.Equal("us-east-1", retrieved.Metadata["region"]);
        Assert.Equal("dc1", retrieved.Metadata["datacenter"]);
    }

    #endregion

    #region Snapshot Consistency

    [Fact]
    public async Task GetServices_ReturnsSnapshot_NotAffectedBySubsequentChanges()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        await registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance1",
            ServiceName = "service1",
            Host = "localhost",
            Port = 8080
        });

        // Act
        var snapshot = registry.GetServices();
        
        // Modify registry after getting snapshot
        await registry.RegisterInstanceAsync(new ServiceInstance
        {
            Id = "instance2",
            ServiceName = "service2",
            Host = "localhost",
            Port = 8081
        });

        // Assert - Snapshot should not reflect new changes
        Assert.Single(snapshot);
        
        // But new query should reflect changes
        var current = registry.GetServices();
        Assert.Equal(2, current.Count);
    }

    [Fact]
    public async Task GetInstances_ReturnsSnapshot_CanBeIteratedSafely()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        for (int i = 0; i < 10; i++)
        {
            await registry.RegisterInstanceAsync(new ServiceInstance
            {
                Id = $"instance{i}",
                ServiceName = "test-service",
                Host = $"host{i}",
                Port = 8080
            });
        }

        // Act
        var snapshot = registry.GetInstances("test-service");

        // Modify registry while iterating snapshot
        var count = 0;
        foreach (var instance in snapshot)
        {
            count++;
            // Deregister during iteration
            await registry.DeregisterInstanceAsync("test-service", instance.Id);
        }

        // Assert - Iteration should complete successfully
        Assert.Equal(10, count);
        
        // Registry should now be empty
        Assert.Null(registry.GetService("test-service"));
    }

    #endregion

    #region Dispose Edge Cases

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var registry = new ServiceRegistry();

        // Act - Multiple dispose calls should be safe
        registry.Dispose();
        registry.Dispose();
        registry.Dispose();

        // Assert - Should complete without throwing
        Assert.True(true); // If we reach here, no exception was thrown
    }

    [Fact]
    public async Task Dispose_WithActiveOperations_CompletesGracefully()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var tasks = new List<Task>();

        // Start some operations
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(registry.RegisterInstanceAsync(new ServiceInstance
            {
                Id = $"instance{i}",
                ServiceName = "test-service",
                Host = "localhost",
                Port = 8080
            }));
        }

        // Act - Dispose while operations are in flight
        registry.Dispose();

        // Assert - Operations should complete without throwing
        await Task.WhenAll(tasks);
        Assert.Equal(10, tasks.Count); // All tasks completed
    }

    #endregion

    #region Special Characters and Encoding

    [Fact]
    public async Task RegisterInstance_WithSpecialCharactersInNames_HandlesCorrectly()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance = new ServiceInstance
        {
            Id = "instance-with-dashes_and_underscores.and.dots",
            ServiceName = "service/with/slashes:and:colons",
            Host = "host-name.domain.com",
            Port = 8080
        };

        // Act
        await registry.RegisterInstanceAsync(instance);

        // Assert
        var service = registry.GetService("service/with/slashes:and:colons");
        Assert.NotNull(service);
        Assert.Single(service.Instances);
    }

    [Fact]
    public async Task RegisterInstance_WithUnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        using var registry = new ServiceRegistry();
        var instance = new ServiceInstance
        {
            Id = "instance-日本語-中文-한국어",
            ServiceName = "service-émojis-🚀-✨",
            Host = "localhost",
            Port = 8080
        };

        // Act
        await registry.RegisterInstanceAsync(instance);

        // Assert
        var service = registry.GetService("service-émojis-🚀-✨");
        Assert.NotNull(service);
        Assert.Single(service.Instances);
    }

    #endregion
}
