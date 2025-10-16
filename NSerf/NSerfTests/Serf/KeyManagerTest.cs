// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/keymanager_test.go

using FluentAssertions;
using NSerf.Serf;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for KeyManager - encryption keyring management across cluster.
/// Note: Full integration tests will be in Phase 9 when Query system is complete.
/// </summary>
public class KeyManagerTest
{
    [Fact]
    public void KeyResponse_DefaultConstruction_ShouldInitializeCollections()
    {
        // Arrange & Act
        var response = new KeyResponse();

        // Assert
        response.Messages.Should().NotBeNull("Messages should be initialized");
        response.Keys.Should().NotBeNull("Keys should be initialized");
        response.PrimaryKeys.Should().NotBeNull("PrimaryKeys should be initialized");
        response.NumNodes.Should().Be(0);
        response.NumResp.Should().Be(0);
        response.NumErr.Should().Be(0);
    }

    [Fact]
    public void KeyResponse_ShouldTrackNodeCounts()
    {
        // Arrange
        var response = new KeyResponse
        {
            NumNodes = 10,
            NumResp = 8,
            NumErr = 2
        };

        // Act & Assert
        response.NumNodes.Should().Be(10);
        response.NumResp.Should().Be(8);
        response.NumErr.Should().Be(2);
    }

    [Fact]
    public void KeyResponse_Messages_ShouldStoreNodeMessages()
    {
        // Arrange
        var response = new KeyResponse();

        // Act
        response.Messages["node1"] = "Success";
        response.Messages["node2"] = "Key installed";

        // Assert
        response.Messages.Should().HaveCount(2);
        response.Messages["node1"].Should().Be("Success");
        response.Messages["node2"].Should().Be("Key installed");
    }

    [Fact]
    public void KeyResponse_Keys_ShouldCountKeyInstallations()
    {
        // Arrange
        var response = new KeyResponse();
        var key1 = "ZWTL+bgjHyQPhJRKcFe3ccirc2SFHmc/Nw67l8NQfdk=";
        var key2 = "WbL6oaTPom+7RG7Q/INbJWKy09OLar/Hf2SuOAdoQE4=";

        // Act
        response.Keys[key1] = 5;  // 5 nodes have key1
        response.Keys[key2] = 3;  // 3 nodes have key2

        // Assert
        response.Keys.Should().HaveCount(2);
        response.Keys[key1].Should().Be(5);
        response.Keys[key2].Should().Be(3);
    }

    [Fact]
    public void KeyResponse_PrimaryKeys_ShouldCountPrimaryKeyUsage()
    {
        // Arrange
        var response = new KeyResponse();
        var primaryKey = "ZWTL+bgjHyQPhJRKcFe3ccirc2SFHmc/Nw67l8NQfdk=";

        // Act
        response.PrimaryKeys[primaryKey] = 10;  // All 10 nodes use this primary key

        // Assert
        response.PrimaryKeys.Should().HaveCount(1);
        response.PrimaryKeys[primaryKey].Should().Be(10);
    }

    [Fact]
    public void KeyRequestOptions_ShouldHaveRelayFactor()
    {
        // Arrange & Act
        var options = new KeyRequestOptions
        {
            RelayFactor = 3
        };

        // Assert
        options.RelayFactor.Should().Be(3);
    }

    [Fact]
    public void KeyRequestOptions_DefaultRelayFactor_ShouldBeZero()
    {
        // Arrange & Act
        var options = new KeyRequestOptions();

        // Assert
        options.RelayFactor.Should().Be(0);
    }

    [Fact]
    public void KeyManager_Constructor_ShouldRequireSerf()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);

        // Act
        var keyManager = new KeyManager(serf);

        // Assert
        keyManager.Should().NotBeNull();
    }

    [Fact]
    public void KeyManager_Constructor_WithNullSerf_ShouldThrow()
    {
        // Act & Assert
        var act = () => new KeyManager(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serf");
    }

    // TODO: Phase 9 - Add integration tests with actual Query system
    // - InstallKey should broadcast query to all nodes
    // - UseKey should change primary key across cluster
    // - RemoveKey should remove key from all keyrings
    // - ListKeys should aggregate keys from all nodes
    // - Error handling when nodes fail to respond
    // - Partial success scenarios (some nodes succeed, some fail)
}
