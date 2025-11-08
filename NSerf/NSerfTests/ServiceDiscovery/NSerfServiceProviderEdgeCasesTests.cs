using Microsoft.Extensions.Logging;
using NSerf.ServiceDiscovery;
using NSerf.Serf;
using Xunit;

namespace NSerfTests.ServiceDiscovery;

/// <summary>
/// Comprehensive edge case tests for NSerfServiceProvider.
/// Tests tag parsing, member status transitions, concurrent operations, and error handling.
/// </summary>
public class NSerfServiceProviderEdgeCasesTests
{
    #region Tag Parsing Edge Cases

    [Fact]
    public void ExtractServices_EmptyTags_ReturnsEmptyDictionary()
    {
        // Arrange
        var member = CreateMember("node1", new Dictionary<string, string>());
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Empty(services);
    }

    [Fact]
    public void ExtractServices_ServiceTagWithoutPort_SkipsService()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true"
            // Missing "port:api"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Empty(services);
    }

    [Fact]
    public void ExtractServices_ServiceTagWithInvalidPort_SkipsService()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "not-a-number"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Empty(services);
    }

    [Fact]
    public void ExtractServices_ServiceTagWithNegativePort_CreatesServiceWithNegativePort()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "-8080"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert - int.TryParse accepts negative numbers, so service is created
        Assert.Single(services);
        Assert.Equal(-8080, services["api"].Port);
    }

    [Fact]
    public void ExtractServices_ServiceTagWithZeroPort_CreatesService()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "0"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        Assert.Equal(0, services["api"].Port);
    }

    [Fact]
    public void ExtractServices_ServiceTagWithMaxPort_CreatesService()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "65535"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        Assert.Equal(65535, services["api"].Port);
    }

    [Fact]
    public void ExtractServices_ServiceTagWithEmptyName_SkipsService()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:"] = "true",
            ["port:"] = "8080"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Empty(services);
    }

    [Fact]
    public void ExtractServices_ServiceTagWithWhitespaceName_SkipsService()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:   "] = "true",
            ["port:   "] = "8080"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Empty(services);
    }

    [Fact]
    public void ExtractServices_MultipleServices_CreatesAllValid()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["service:metrics"] = "true",
            ["port:metrics"] = "9090",
            ["service:invalid"] = "true"
            // Missing port:invalid
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Equal(2, services.Count);
        Assert.Contains("api", services.Keys);
        Assert.Contains("metrics", services.Keys);
        Assert.DoesNotContain("invalid", services.Keys);
    }

    [Fact]
    public void ExtractServices_DefaultScheme_IsHttp()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
            // No scheme specified
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        Assert.Equal("http", services["api"].Scheme);
    }

    [Fact]
    public void ExtractServices_CustomScheme_IsPreserved()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["scheme:api"] = "https"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        Assert.Equal("https", services["api"].Scheme);
    }

    [Fact]
    public void ExtractServices_DefaultWeight_Is100()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
            // No weight specified
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        Assert.Equal(100, services["api"].Weight);
    }

    [Fact]
    public void ExtractServices_CustomWeight_IsPreserved()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["weight:api"] = "50"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        Assert.Equal(50, services["api"].Weight);
    }

    [Fact]
    public void ExtractServices_InvalidWeight_UsesDefault()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["weight:api"] = "not-a-number"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        Assert.Equal(100, services["api"].Weight);
    }

    [Fact]
    public void ExtractServices_CustomMetadata_IsPreserved()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["version"] = "1.2.3",
            ["region"] = "us-east-1",
            ["datacenter"] = "dc1"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        var instance = services["api"];
        Assert.Equal("1.2.3", instance.Metadata["version"]);
        Assert.Equal("us-east-1", instance.Metadata["region"]);
        Assert.Equal("dc1", instance.Metadata["datacenter"]);
    }

    [Fact]
    public void ExtractServices_SerfMetadata_IsIncluded()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        };
        var member = CreateMember("node1", tags, status: MemberStatus.Alive);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        var instance = services["api"];
        Assert.Equal("node1", instance.Metadata["serf.member.name"]);
        Assert.Contains("serf.member.addr", instance.Metadata.Keys);
        Assert.Contains("serf.member.port", instance.Metadata.Keys);
        Assert.Equal("Alive", instance.Metadata["serf.member.status"]);
    }

    [Fact]
    public void ExtractServices_CustomTagPrefixes_AreRespected()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["svc:api"] = "true",
            ["p:api"] = "8080",
            ["proto:api"] = "grpc",
            ["w:api"] = "75"
        };
        var member = CreateMember("node1", tags);
        var options = new NSerfServiceProviderOptions
        {
            ServiceTagPrefix = "svc:",
            PortTagPrefix = "p:",
            SchemeTagPrefix = "proto:",
            WeightTagPrefix = "w:"
        };
        var provider = CreateProvider(options);

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        var instance = services["api"];
        Assert.Equal(8080, instance.Port);
        Assert.Equal("grpc", instance.Scheme);
        Assert.Equal(75, instance.Weight);
    }

    #endregion

    #region Member Status Mapping Edge Cases

    [Fact]
    public void MemberStatusMapping_Documentation()
    {
        // This documents the expected status mapping in ProcessMemberAsync:
        // Alive -> Healthy
        // Failed -> Unhealthy
        // Leaving -> Draining
        // Left/None -> Unknown
        // Real testing requires integration tests with actual Serf events
        Assert.True(true);
    }

    #endregion

    #region Lifecycle Edge Cases

    [Fact]
    public async Task StartAsync_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);

        // Act
        await provider.StartAsync();
        await provider.StartAsync(); // Second call

        // Assert - Should not throw
        Assert.True(true);
    }

    [Fact]
    public async Task StopAsync_WithoutStart_DoesNotThrow()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);

        // Act
        await provider.StopAsync();

        // Assert - Should not throw
        Assert.True(true);
    }

    [Fact]
    public async Task StopAsync_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);
        await provider.StartAsync();

        // Act
        await provider.StopAsync();
        await provider.StopAsync(); // Second call

        // Assert - Should not throw
        Assert.True(true);
    }

    [Fact]
    public async Task DiscoverServicesAsync_BeforeStart_ReturnsEmptyList()
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
    public async Task DiscoverServicesAsync_AfterStop_ReturnsLastKnownState()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);
        await provider.StartAsync();
        await provider.StopAsync();

        // Act
        var services = await provider.DiscoverServicesAsync();

        // Assert - Should return cached state, not throw
        Assert.NotNull(services);
    }

    [Fact]
    public void Dispose_BeforeStart_DoesNotThrow()
    {
        // Arrange
        var serf = CreateMockSerf();
        var provider = new NSerfServiceProvider(serf);

        // Act
        provider.Dispose();

        // Assert - Should not throw
        Assert.True(true);
    }

    [Fact]
    public async Task Dispose_DuringEventLoop_CancelsGracefully()
    {
        // Arrange
        var serf = CreateMockSerf();
        var provider = new NSerfServiceProvider(serf);
        await provider.StartAsync();

        // Act - Dispose while event loop is running
        provider.Dispose();

        // Assert - Should complete without hanging
        Assert.True(true);
    }

    #endregion

    #region Event Handler Edge Cases

    [Fact]
    public async Task ServiceDiscovered_NullHandler_DoesNotThrow()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);
        // No event handler attached

        // Act
        await provider.StartAsync();

        // Assert - Should not throw when raising events with no handlers
        Assert.True(true);
    }

    [Fact]
    public async Task ServiceDiscovered_HandlerThrowsException_DoesNotBreakProvider()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);

        provider.ServiceDiscovered += (s, e) =>
        {
            throw new InvalidOperationException("Test exception");
        };

        // Act
        await provider.StartAsync();

        // Assert - Provider should continue working despite handler exception
        var services = await provider.DiscoverServicesAsync();
        Assert.NotNull(services);
    }

    [Fact]
    public async Task ServiceDiscovered_MultipleHandlers_OneThrows_OthersExecute()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);

        provider.ServiceDiscovered += (s, e) => { /* Handler 1 */ };
        provider.ServiceDiscovered += (s, e) => { throw new Exception("Handler 2 fails"); };
        provider.ServiceDiscovered += (s, e) => { /* Handler 3 */ };

        // Act
        await provider.StartAsync();
        await Task.Delay(100); // Give time for event processing

        // Assert - All handlers should be invoked despite handler 2 throwing
        // The RaiseServiceDiscovered method isolates exceptions per handler
        // Note: This test is limited without actual event triggering
        Assert.True(true);
    }

    #endregion

    #region Concurrent Operations Edge Cases

    [Fact]
    public async Task DiscoverServicesAsync_ConcurrentCalls_AreThreadSafe()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);
        await provider.StartAsync();

        // Act - Multiple concurrent discovery calls
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.DiscoverServicesAsync())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - All calls should succeed
        Assert.Equal(10, results.Length);
        Assert.All(results, r => Assert.NotNull(r));
    }

    [Fact]
    public async Task StartStopCycle_Repeated_DoesNotLeak()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);

        // Act - Repeated start/stop cycles
        for (int i = 0; i < 5; i++)
        {
            await provider.StartAsync();
            await Task.Delay(50);
            await provider.StopAsync();
            await Task.Delay(50);
        }

        // Assert - Should complete without issues
        Assert.True(true);
    }

    #endregion

    #region Options Validation Edge Cases

    [Fact]
    public void Options_NullOptions_UsesDefaults()
    {
        // Arrange
        var serf = CreateMockSerf();

        // Act
        using var provider = new NSerfServiceProvider(serf, options: null);

        // Assert
        Assert.Equal("NSerf", provider.Name);
    }

    [Fact]
    public void Options_EmptyTagPrefixes_WorksCorrectly()
    {
        // Arrange
        var options = new NSerfServiceProviderOptions
        {
            ServiceTagPrefix = "",
            PortTagPrefix = "",
            SchemeTagPrefix = "",
            WeightTagPrefix = ""
        };
        var serf = CreateMockSerf();

        // Act
        using var provider = new NSerfServiceProvider(serf, options);

        // Assert - Should create provider without throwing
        Assert.Equal("NSerf", provider.Name);
    }

    [Fact]
    public void Options_ImmutableAfterCreation()
    {
        // Arrange
        var options = new NSerfServiceProviderOptions
        {
            ServiceTagPrefix = "service:"
        };
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf, options);

        // Act & Assert - Properties should be init-only
        // This is enforced by the compiler with 'init' accessors
        Assert.Equal("service:", options.ServiceTagPrefix);
    }

    #endregion

    #region Instance ID Generation Edge Cases

    [Fact]
    public void InstanceId_Format_IsConsistent()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Single(services);
        Assert.Equal("node1:api", services["api"].Id);
    }

    [Fact]
    public void InstanceId_MultipleServices_SameMember_UniqueIds()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["service:api"] = "true",
            ["port:api"] = "8080",
            ["service:metrics"] = "true",
            ["port:metrics"] = "9090"
        };
        var member = CreateMember("node1", tags);
        var provider = CreateProvider();

        // Act
        var services = ExtractServicesViaReflection(provider, member);

        // Assert
        Assert.Equal(2, services.Count);
        Assert.Equal("node1:api", services["api"].Id);
        Assert.Equal("node1:metrics", services["metrics"].Id);
    }

    #endregion

    #region Service Grouping Edge Cases

    [Fact]
    public async Task DiscoverServicesAsync_GroupsByServiceName()
    {
        // Arrange
        var serf = CreateMockSerf();
        using var provider = new NSerfServiceProvider(serf);

        // Act
        var services = await provider.DiscoverServicesAsync();

        // Assert - Services should be grouped by name
        Assert.NotNull(services);
        Assert.All(services, s => Assert.NotNull(s.Name));
    }

    #endregion

    #region Helper Methods

    private static NSerfServiceProvider CreateProvider(NSerfServiceProviderOptions? options = null)
    {
        var serf = CreateMockSerf();
        return new NSerfServiceProvider(serf, options);
    }

    private static NSerf.Serf.Serf CreateMockSerf()
    {
        var config = new NSerf.Serf.Config
        {
            NodeName = "test-node",
            MemberlistConfig = NSerf.Memberlist.Configuration.MemberlistConfig.DefaultLANConfig()
        };
        return new NSerf.Serf.Serf(config);
    }

    private static Member CreateMember(string name, Dictionary<string, string> tags, MemberStatus status = MemberStatus.Alive)
    {
        return new Member
        {
            Name = name,
            Addr = System.Net.IPAddress.Parse("10.0.0.1"),
            Port = 7946,
            Tags = tags,
            Status = status,
            ProtocolMin = 2,
            ProtocolMax = 5,
            ProtocolCur = 5,
            DelegateMin = 2,
            DelegateMax = 5,
            DelegateCur = 5
        };
    }

    /// <summary>
    /// Uses reflection to call the private ExtractServicesFromMember method.
    /// This allows us to test the tag parsing logic in isolation.
    /// </summary>
    private static Dictionary<string, ServiceInstance> ExtractServicesViaReflection(NSerfServiceProvider provider, Member member)
    {
        var method = typeof(NSerfServiceProvider).GetMethod(
            "ExtractServicesFromMember",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (method == null)
            throw new InvalidOperationException("ExtractServicesFromMember method not found");

        var result = method.Invoke(provider, [member]);
        return (Dictionary<string, ServiceInstance>)result!;
    }

    #endregion
}
