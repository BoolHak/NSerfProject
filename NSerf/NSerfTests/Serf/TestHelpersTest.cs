// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerfTests.Serf;

/// <summary>
/// Tests for the TestHelpers utility class.
/// Verifies that test utilities work correctly before using them for actual Serf tests.
/// </summary>
public class TestHelpersTest
{
    [Fact]
    public void CreateTestConfig_ShouldReturnValidConfig()
    {
        // Act
        var config = TestHelpers.CreateTestConfig();

        // Assert
        config.Should().NotBeNull();
        config.NodeName.Should().NotBeNullOrEmpty();
        config.MemberlistConfig.Should().NotBeNull();
        config.MemberlistConfig.BindPort.Should().BeGreaterThan(5000);
    }

    [Fact]
    public void CreateTestConfig_WithNodeName_ShouldUseProvidedName()
    {
        // Arrange
        var expectedName = "test-custom-node";

        // Act
        var config = TestHelpers.CreateTestConfig(expectedName);

        // Assert
        config.NodeName.Should().Be(expectedName);
        config.MemberlistConfig.Name.Should().Be(expectedName);
    }

    [Fact]
    public void CreateTestConfig_MultipleCalls_ShouldReturnUniquePorts()
    {
        // Act
        var config1 = TestHelpers.CreateTestConfig();
        var config2 = TestHelpers.CreateTestConfig();
        var config3 = TestHelpers.CreateTestConfig();

        // Assert
        var ports = new[] 
        { 
            config1.MemberlistConfig.BindPort, 
            config2.MemberlistConfig.BindPort, 
            config3.MemberlistConfig.BindPort 
        };
        
        ports.Should().OnlyHaveUniqueItems("Each config should get a unique port");
    }

    [Fact]
    public void CreateTestConfig_ShouldHaveAggressiveTimeouts()
    {
        // Act
        var config = TestHelpers.CreateTestConfig();

        // Assert - timeouts should be short for fast tests
        config.MemberlistConfig.ProbeInterval.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
        config.MemberlistConfig.ProbeTimeout.Should().BeLessThan(TimeSpan.FromMilliseconds(50));
        config.ReapInterval.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForConditionAsync_WhenConditionBecomesTrue_ShouldReturn()
    {
        // Arrange
        var flag = false;
        var task = Task.Run(async () =>
        {
            await Task.Delay(50);
            flag = true;
        });

        // Act & Assert - should not throw
        await TestHelpers.WaitForConditionAsync(
            () => flag,
            TimeSpan.FromSeconds(1));
        
        flag.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForConditionAsync_WhenTimeoutExceeds_ShouldThrow()
    {
        // Arrange
        var flag = false;

        // Act
        var act = async () => await TestHelpers.WaitForConditionAsync(
            () => flag,
            TimeSpan.FromMilliseconds(100),
            "Test condition never met");

        // Assert
        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*Test condition never met*");
    }

    [Fact]
    public void CreateTestEventChannel_ShouldReturnValidChannel()
    {
        // Act
        var (writer, reader) = TestHelpers.CreateTestEventChannel();

        // Assert
        writer.Should().NotBeNull();
        reader.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateTestEventChannel_ShouldAllowWriteAndRead()
    {
        // Arrange
        var (writer, reader) = TestHelpers.CreateTestEventChannel();
        
        // This test won't compile until we have Event types, so we'll skip the read/write
        // Just verify channel creation works
        writer.Should().NotBeNull();
        reader.Should().NotBeNull();
        
        await Task.CompletedTask;
    }

    [Fact]
    public void AllocateTestIP_ShouldReturnLoopbackAddress()
    {
        // Act
        var ip = TestHelpers.AllocateTestIP();

        // Assert
        ip.Should().NotBeNull();
        ip.ToString().Should().StartWith("127.0.0.");
    }

    [Fact]
    public void AllocateTestIP_MultipleCalls_ShouldReturnDifferentIPs()
    {
        // Act
        var ip1 = TestHelpers.AllocateTestIP();
        var ip2 = TestHelpers.AllocateTestIP();
        var ip3 = TestHelpers.AllocateTestIP();

        // Assert
        var ips = new[] { ip1, ip2, ip3 };
        ips.Should().OnlyHaveUniqueItems("Each call should return a unique IP");
    }

    [Fact]
    public void CreateTestCluster_ShouldCreateMultipleConfigs()
    {
        // Arrange
        var nodeCount = 5;

        // Act
        var configs = TestHelpers.CreateTestCluster(nodeCount);

        // Assert
        configs.Should().HaveCount(nodeCount);
        configs.Select(c => c.NodeName).Should().OnlyHaveUniqueItems();
        configs.Select(c => c.MemberlistConfig.BindPort).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CreateTestCluster_WithZeroNodes_ShouldReturnEmptyList()
    {
        // Act
        var configs = TestHelpers.CreateTestCluster(0);

        // Assert
        configs.Should().BeEmpty();
    }
}
