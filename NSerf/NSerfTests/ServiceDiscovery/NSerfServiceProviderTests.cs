using Microsoft.Extensions.Logging;
using NSerf.ServiceDiscovery;
using NSerf.Serf;
using Xunit;

namespace NSerfTests.ServiceDiscovery;

/// <summary>
/// Tests for NSerfServiceProvider integration with Serf cluster.
/// </summary>
public class NSerfServiceProviderTests
{
    [Fact]
    public void Constructor_NullSerf_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new NSerfServiceProvider(null!));
    }

    [Fact]
    public void Name_ReturnsNSerf()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);

        // Act
        var name = provider.Name;

        // Assert
        Assert.Equal("NSerf", name);
    }

    [Fact]
    public async Task StartAsync_InitializesSuccessfully()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);

        // Act
        await provider.StartAsync();

        // Assert - Should complete without throwing
        Assert.True(true);
    }

    [Fact]
    public async Task StopAsync_StopsSuccessfully()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);
        await provider.StartAsync();

        // Act
        await provider.StopAsync();

        // Assert - Should complete without throwing
        Assert.True(true);
    }

    [Fact]
    public async Task DiscoverServicesAsync_NoMembers_ReturnsEmptyList()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);

        // Act
        var services = await provider.DiscoverServicesAsync();

        // Assert
        Assert.Empty(services);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var serf = CreateMockSerf();
        var provider = new NSerfServiceProvider(serf);

        // Act - Multiple dispose calls should be safe
        provider.Dispose();
        provider.Dispose();
        provider.Dispose();

        // Assert - Should complete without throwing
        Assert.True(true);
    }

    [Fact]
    public async Task StartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var serf = CreateMockSerf();
        var provider = new NSerfServiceProvider(serf);
        provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await provider.StartAsync());
    }

    [Fact]
    public async Task StopAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var serf = CreateMockSerf();
        var provider = new NSerfServiceProvider(serf);
        provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await provider.StopAsync());
    }

    [Fact]
    public async Task DiscoverServicesAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var serf = CreateMockSerf();
        var provider = new NSerfServiceProvider(serf);
        provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await provider.DiscoverServicesAsync());
    }

    [Fact]
    public void Options_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new NSerfServiceProviderOptions();

        // Assert
        Assert.Equal("service:", options.ServiceTagPrefix);
        Assert.Equal("port:", options.PortTagPrefix);
        Assert.Equal("scheme:", options.SchemeTagPrefix);
        Assert.Equal("weight:", options.WeightTagPrefix);
        Assert.False(options.EnableUserEventDiscovery);
        Assert.Equal("service:", options.UserEventPrefix);
        Assert.True(options.AutoMarkFailedUnhealthy);
        Assert.True(options.AutoDeregisterOnLeave);
    }

    [Fact]
    public void Options_CustomValues_AreRespected()
    {
        // Arrange & Act
        var options = new NSerfServiceProviderOptions
        {
            ServiceTagPrefix = "svc:",
            PortTagPrefix = "p:",
            SchemeTagPrefix = "proto:",
            WeightTagPrefix = "w:",
            EnableUserEventDiscovery = true,
            UserEventPrefix = "svc:",
            AutoMarkFailedUnhealthy = false,
            AutoDeregisterOnLeave = false
        };

        // Assert
        Assert.Equal("svc:", options.ServiceTagPrefix);
        Assert.Equal("p:", options.PortTagPrefix);
        Assert.Equal("proto:", options.SchemeTagPrefix);
        Assert.Equal("w:", options.WeightTagPrefix);
        Assert.True(options.EnableUserEventDiscovery);
        Assert.Equal("svc:", options.UserEventPrefix);
        Assert.False(options.AutoMarkFailedUnhealthy);
        Assert.False(options.AutoDeregisterOnLeave);
    }

    /// <summary>
    /// Creates a minimal mock Serf instance for testing.
    /// Note: This is a placeholder - real integration tests would use actual Serf instances.
    /// </summary>
    private static NSerf.Serf.Serf CreateMockSerf()
    {
        var config = new NSerf.Serf.Config
        {
            NodeName = "test-node",
            MemberlistConfig = NSerf.Memberlist.Configuration.MemberlistConfig.DefaultLANConfig()
        };

        // Create a minimal Serf instance for testing
        // In real scenarios, you'd use Serf.CreateAsync()
        var serf = new NSerf.Serf.Serf(config);
        return serf;
    }
}
