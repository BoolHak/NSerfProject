// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/keymanager_test.go

using FluentAssertions;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using Xunit;
using SerfSerf = NSerf.Serf.Serf;
using Config = NSerf.Serf.Config;
using KeyManager = NSerf.Serf.KeyManager;
using KeyResponse = NSerf.Serf.KeyResponse;
using KeyRequestOptions = NSerf.Serf.KeyRequestOptions;

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

    [Fact]
    public async Task KeyManager_InstallKey_ShouldPropagateToAllNodes()
    {
        // Arrange - Create 3-node cluster with encryption
        var key1 = Convert.ToBase64String(new byte[32] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 });
        
        var serf1 = await CreateSerfWithEncryption("node1", key1);
        var serf2 = await CreateSerfWithEncryption("node2", key1);
        var serf3 = await CreateSerfWithEncryption("node3", key1);
        
        try
        {
            // Join cluster
            var addr1 = $"{serf1.Memberlist!.LocalNode.Addr}:{serf1.Memberlist.LocalNode.Port}";
            await serf2.JoinAsync(new[] { addr1 }, false);
            await serf3.JoinAsync(new[] { addr1 }, false);
            
            // Wait for cluster to stabilize via push-pull and gossip
            // With PushPullInterval=2s, nodes will sync within a few seconds
            var maxWait = TimeSpan.FromSeconds(10);
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < maxWait)
            {
                if (serf1.NumMembers() == 3 && serf2.NumMembers() == 3 && serf3.NumMembers() == 3)
                    break;
                await Task.Delay(200); // Wait for push-pull cycle
            }
            
            // Verify all nodes see each other
            serf1.NumMembers().Should().Be(3, "node1 should see all 3 members");
            serf2.NumMembers().Should().Be(3, "node2 should see all 3 members");
            serf3.NumMembers().Should().Be(3, "node3 should see all 3 members");
            
            // Act - Install a new key from node1
            var newKey = Convert.ToBase64String(new byte[32] { 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 });
            var manager = new KeyManager(serf1);
            var response = await manager.InstallKey(newKey);
            
            // Assert - Check response
            response.NumNodes.Should().Be(3, "should target all 3 nodes");
            response.NumResp.Should().Be(3, "should receive 3 responses");
            response.NumErr.Should().Be(0, "should have no errors");
            
            // Verify new key installed on all nodes
            await Task.Delay(200); // Allow time for propagation
            
            var keys1 = serf1.Config.MemberlistConfig!.Keyring!.GetKeys();
            var keys2 = serf2.Config.MemberlistConfig!.Keyring!.GetKeys();
            var keys3 = serf3.Config.MemberlistConfig!.Keyring!.GetKeys();
            
            keys1.Should().HaveCount(2, "node1 should have 2 keys");
            keys2.Should().HaveCount(2, "node2 should have 2 keys");
            keys3.Should().HaveCount(2, "node3 should have 2 keys");
            
            var newKeyBytes = Convert.FromBase64String(newKey);
            keys1.Should().ContainEquivalentOf(newKeyBytes, "node1 should have new key");
            keys2.Should().ContainEquivalentOf(newKeyBytes, "node2 should have new key");
            keys3.Should().ContainEquivalentOf(newKeyBytes, "node3 should have new key");
        }
        finally
        {
            await serf1.ShutdownAsync();
            await serf2.ShutdownAsync();
            await serf3.ShutdownAsync();
            await serf1.DisposeAsync();
            await serf2.DisposeAsync();
            await serf3.DisposeAsync();
        }
    }
    
    [Fact]
    public async Task KeyManager_UseKey_ShouldChangePrimaryKeyAcrossCluster()
    {
        // Arrange - Create 2-node cluster with multiple keys
        var key1 = Convert.ToBase64String(new byte[32] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 });
        var key2 = Convert.ToBase64String(new byte[32] { 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 });
        
        var serf1 = await CreateSerfWithEncryption("node1", key1);
        var serf2 = await CreateSerfWithEncryption("node2", key1);
        
        try
        {
            // Join cluster
            var addr1 = $"{serf1.Memberlist!.LocalNode.Addr}:{serf1.Memberlist.LocalNode.Port}";
            await serf2.JoinAsync(new[] { addr1 }, false);
            // Wait for cluster formation
            var maxWait = TimeSpan.FromSeconds(10);
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < maxWait)
            {
                if (serf1.NumMembers() == 2 && serf2.NumMembers() == 2)
                    break;
                await Task.Delay(200);
            }
            
            // Install second key on both nodes
            var manager = new KeyManager(serf1);
            await manager.InstallKey(key2);
            await Task.Delay(200);
            
            // Verify key1 is still primary
            var primaryKey1Before = serf1.Config.MemberlistConfig!.Keyring!.GetPrimaryKey();
            var primaryKey2Before = serf2.Config.MemberlistConfig!.Keyring!.GetPrimaryKey();
            Convert.ToBase64String(primaryKey1Before!).Should().Be(key1);
            Convert.ToBase64String(primaryKey2Before!).Should().Be(key1);
            
            // Act - Change primary key to key2
            var response = await manager.UseKey(key2);
            
            // Assert
            response.NumErr.Should().Be(0, "should have no errors");
            response.NumResp.Should().BeGreaterThan(0, "should receive responses");
            
            await Task.Delay(200);
            
            // Verify key2 is now primary on all nodes
            var primaryKey1After = serf1.Config.MemberlistConfig!.Keyring!.GetPrimaryKey();
            var primaryKey2After = serf2.Config.MemberlistConfig!.Keyring!.GetPrimaryKey();
            Convert.ToBase64String(primaryKey1After!).Should().Be(key2, "node1 should have key2 as primary");
            Convert.ToBase64String(primaryKey2After!).Should().Be(key2, "node2 should have key2 as primary");
        }
        finally
        {
            await serf1.ShutdownAsync();
            await serf2.ShutdownAsync();
            await serf1.DisposeAsync();
            await serf2.DisposeAsync();
        }
    }
    
    [Fact]
    public async Task KeyManager_RemoveKey_ShouldRemoveFromAllNodes()
    {
        // Arrange - Create 2-node cluster with 2 keys
        var key1 = Convert.ToBase64String(new byte[32] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 });
        var key2 = Convert.ToBase64String(new byte[32] { 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 });
        
        var serf1 = await CreateSerfWithEncryption("node1", key1);
        var serf2 = await CreateSerfWithEncryption("node2", key1);
        
        try
        {
            var addr1 = $"{serf1.Memberlist!.LocalNode.Addr}:{serf1.Memberlist.LocalNode.Port}";
            await serf2.JoinAsync(new[] { addr1 }, false);
            // Wait for cluster formation
            var maxWait = TimeSpan.FromSeconds(10);
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < maxWait)
            {
                if (serf1.NumMembers() == 2 && serf2.NumMembers() == 2)
                    break;
                await Task.Delay(200);
            }
            
            // Install and use second key
            var manager = new KeyManager(serf1);
            await manager.InstallKey(key2);
            await manager.UseKey(key2);
            await Task.Delay(200);
            
            // Verify both keys exist
            serf1.Config.MemberlistConfig!.Keyring!.GetKeys().Should().HaveCount(2);
            serf2.Config.MemberlistConfig!.Keyring!.GetKeys().Should().HaveCount(2);
            
            // Act - Remove old key1 (not primary)
            var response = await manager.RemoveKey(key1);
            
            // Assert
            response.NumErr.Should().Be(0, "should have no errors");
            await Task.Delay(200);
            
            // Verify key1 removed from all nodes
            var keys1 = serf1.Config.MemberlistConfig!.Keyring!.GetKeys();
            var keys2 = serf2.Config.MemberlistConfig!.Keyring!.GetKeys();
            
            keys1.Should().HaveCount(1, "node1 should have 1 key after removal");
            keys2.Should().HaveCount(1, "node2 should have 1 key after removal");
            
            var key1Bytes = Convert.FromBase64String(key1);
            keys1.Should().NotContainEquivalentOf(key1Bytes, "node1 should not have key1");
            keys2.Should().NotContainEquivalentOf(key1Bytes, "node2 should not have key1");
        }
        finally
        {
            await serf1.ShutdownAsync();
            await serf2.ShutdownAsync();
            await serf1.DisposeAsync();
            await serf2.DisposeAsync();
        }
    }
    
    [Fact]
    public async Task KeyManager_ListKeys_ShouldAggregateKeysFromAllNodes()
    {
        // Arrange - Create 3-node cluster
        var key1 = Convert.ToBase64String(new byte[32] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 });
        
        var serf1 = await CreateSerfWithEncryption("node1", key1);
        var serf2 = await CreateSerfWithEncryption("node2", key1);
        var serf3 = await CreateSerfWithEncryption("node3", key1);
        
        try
        {
            var addr1 = $"{serf1.Memberlist!.LocalNode.Addr}:{serf1.Memberlist.LocalNode.Port}";
            await serf2.JoinAsync(new[] { addr1 }, false);
            await serf3.JoinAsync(new[] { addr1 }, false);
            
            // Wait for cluster formation
            var maxWait = TimeSpan.FromSeconds(10);
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < maxWait)
            {
                if (serf1.NumMembers() == 3 && serf2.NumMembers() == 3 && serf3.NumMembers() == 3)
                    break;
                await Task.Delay(200);
            }
            
            // Act - List keys from node1
            var manager = new KeyManager(serf1);
            var response = await manager.ListKeys();
            
            // Assert
            response.NumNodes.Should().Be(3, "should query 3 nodes");
            response.NumResp.Should().Be(3, "should receive 3 responses");
            response.NumErr.Should().Be(0, "should have no errors");
            
            // Should have key1 reported by all 3 nodes
            response.Keys.Should().ContainKey(key1);
            response.Keys[key1].Should().Be(3, "all 3 nodes should have key1");
            
            // Primary keys should all be key1
            response.PrimaryKeys.Should().ContainKey(key1);
            response.PrimaryKeys[key1].Should().Be(3, "all 3 nodes should have key1 as primary");
        }
        finally
        {
            await serf1.ShutdownAsync();
            await serf2.ShutdownAsync();
            await serf3.ShutdownAsync();
            await serf1.DisposeAsync();
            await serf2.DisposeAsync();
            await serf3.DisposeAsync();
        }
    }
    
    [Fact]
    public async Task KeyManager_RemovePrimaryKey_ShouldFailOnAllNodes()
    {
        // Arrange
        var key1 = Convert.ToBase64String(new byte[32] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 });
        var serf1 = await CreateSerfWithEncryption("node1", key1);
        var serf2 = await CreateSerfWithEncryption("node2", key1);
        
        try
        {
            var addr1 = $"{serf1.Memberlist!.LocalNode.Addr}:{serf1.Memberlist.LocalNode.Port}";
            await serf2.JoinAsync(new[] { addr1 }, false);
            // Wait for cluster formation
            var maxWait = TimeSpan.FromSeconds(10);
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < maxWait)
            {
                if (serf1.NumMembers() == 2 && serf2.NumMembers() == 2)
                    break;
                await Task.Delay(200);
            }
            
            // Act - Try to remove primary key
            var manager = new KeyManager(serf1);
            var response = await manager.RemoveKey(key1);
            
            // Assert - Should fail on all nodes
            response.NumErr.Should().Be(2, "both nodes should report error");
            response.Messages.Values.Should().OnlyContain(msg => msg.Contains("primary") || msg.Contains("Cannot remove"), 
                "error messages should mention primary key");
            
            // Verify key still exists on all nodes
            serf1.Config.MemberlistConfig!.Keyring!.GetKeys().Should().HaveCount(1);
            serf2.Config.MemberlistConfig!.Keyring!.GetKeys().Should().HaveCount(1);
        }
        finally
        {
            await serf1.ShutdownAsync();
            await serf2.ShutdownAsync();
            await serf1.DisposeAsync();
            await serf2.DisposeAsync();
        }
    }
    
    [Fact]
    public async Task KeyManager_WithoutEncryption_ShouldReturnError()
    {
        // Arrange - Create node without encryption
        var config = new Config
        {
            NodeName = "node1",
            Tags = new Dictionary<string, string>(),
            MemberlistConfig = MemberlistConfig.DefaultLANConfig()
        };
        config.MemberlistConfig.BindAddr = "127.0.0.1";
        config.MemberlistConfig.BindPort = 0;
        
        var serf = await SerfSerf.CreateAsync(config);
        
        try
        {
            // Act - Try key operations without encryption
            var manager = new KeyManager(serf);
            var response = await manager.ListKeys();
            
            // Assert
            response.NumErr.Should().Be(1, "should have error from single node");
            response.Messages.Values.Should().OnlyContain(msg => msg.Contains("not enabled") || msg.Contains("empty"),
                "error should mention encryption not enabled");
        }
        finally
        {
            await serf.ShutdownAsync();
            await serf.DisposeAsync();
        }
    }
    
    private static async Task<SerfSerf> CreateSerfWithEncryption(string nodeName, string base64Key)
    {
        var config = new Config
        {
            NodeName = nodeName,
            Tags = new Dictionary<string, string>(),
            MemberlistConfig = MemberlistConfig.DefaultLANConfig()
        };
        config.MemberlistConfig.BindAddr = "127.0.0.1";
        config.MemberlistConfig.BindPort = 0;
        
        // Speed up cluster formation for tests
        config.MemberlistConfig.PushPullInterval = TimeSpan.FromSeconds(2);
        config.MemberlistConfig.GossipInterval = TimeSpan.FromMilliseconds(100);
        
        // Create keyring with initial key
        var keyBytes = Convert.FromBase64String(base64Key);
        var keyring = Keyring.Create(null, keyBytes);
        config.MemberlistConfig.Keyring = keyring;
        
        // CRITICAL: Enable gossip verification for encrypted messages
        config.MemberlistConfig.GossipVerifyIncoming = true;
        config.MemberlistConfig.GossipVerifyOutgoing = true;
        
        return await SerfSerf.CreateAsync(config);
    }
}
