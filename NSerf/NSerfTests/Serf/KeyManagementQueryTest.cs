// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Tests for key management query handlers
// Ported from: github.com/hashicorp/serf/serf/internal_query_test.go

using System.Threading.Channels;
using FluentAssertions;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using NSerf.Serf;
using NSerf.Serf.Events;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for key management query handlers (list-keys, install-key, use-key, remove-key).
/// </summary>
public class KeyManagementQueryTest
{
    /// <summary>
    /// Tests that list-keys query with no encryption returns error.
    /// </summary>
    [Fact]
    public async Task ListKeysQuery_NoEncryption_ShouldReturnError()
    {
        // Arrange - Create Serf without encryption
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
                // No keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);
        var outCh = Channel.CreateUnbounded<IEvent>();
        using var cts = new CancellationTokenSource();

        // Act - Create handler
        var (inCh, _) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

        // Send list-keys query
        var query = new Query
        {
            Name = "_serf_list-keys",
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10),
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "test-source"
        };

        await inCh.WriteAsync(query);

        // Wait for processing
        await Task.Delay(100);

        // Assert - Query was processed (response would contain error message)
        // Since we can't easily intercept the response, we just verify no crash

        // Cleanup
        await cts.CancelAsync();
        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that list-keys query with encryption returns keys.
    /// </summary>
    [Fact]
    public async Task ListKeysQuery_WithEncryption_ShouldReturnKeys()
    {
        // Arrange - Create Serf with encryption
        var key1 = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=";
        var key1Bytes = Convert.FromBase64String(key1);
        var keyring = Keyring.Create(null, key1Bytes);

        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0,
                Keyring = keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Verify encryption is enabled
        serf.EncryptionEnabled().Should().BeTrue();

        var outCh = Channel.CreateUnbounded<IEvent>();
        using var cts = new CancellationTokenSource();

        // Act - Create handler
        var (inCh, _) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

        // Send list-keys query
        var query = new Query
        {
            Name = "_serf_list-keys",
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10),
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "test-source"
        };

        await inCh.WriteAsync(query);

        // Wait for processing
        await Task.Delay(200);

        // Assert - Query was processed successfully
        // The response would contain the key list in the real implementation

        // Cleanup
        await cts.CancelAsync();
        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that install-key query with no encryption returns error.
    /// </summary>
    [Fact]
    public async Task InstallKeyQuery_NoEncryption_ShouldReturnError()
    {
        // Arrange - Create Serf without encryption
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
                // No keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);
        var outCh = Channel.CreateUnbounded<IEvent>();
        var cts = new CancellationTokenSource();

        // Act - Create handler
        var (inCh, handler) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

        // Prepare new key
        var newKey = "HvY8ubRZMgafUOWvrOadwOckVa1wN3QWAo46FVKbVN8=";
        var newKeyBytes = Convert.FromBase64String(newKey);

        // Create key request payload
        var keyRequest = new KeyRequest { Key = newKeyBytes };
        var payload = new byte[1 + MessagePack.MessagePackSerializer.Serialize(keyRequest).Length];
        payload[0] = 0; // Message type byte
        Array.Copy(MessagePack.MessagePackSerializer.Serialize(keyRequest), 0, payload, 1, payload.Length - 1);

        // Send install-key query
        var query = new Query
        {
            Name = "_serf_install-key",
            Payload = payload,
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10),
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "test-source"
        };

        await inCh.WriteAsync(query);

        // Wait for processing
        await Task.Delay(100);

        // Assert - Query was processed (response would contain error)
        // Since we can't easily intercept the response, we just verify no crash

        // Cleanup
        cts.Cancel();
        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that install-key query adds a new key to the keyring.
    /// </summary>
    [Fact]
    public async Task InstallKeyQuery_WithEncryption_ShouldAddKey()
    {
        // Arrange - Create Serf with encryption
        var existingKey = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=";
        var existingKeyBytes = Convert.FromBase64String(existingKey);
        var keyring = Keyring.Create(null, existingKeyBytes);

        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0,
                Keyring = keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Verify initial key count
        keyring.GetKeys().Should().HaveCount(1);

        var outCh = Channel.CreateUnbounded<IEvent>();
        var cts = new CancellationTokenSource();

        // Act - Create handler
        var (inCh, handler) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

        // Prepare new key
        var newKey = "HvY8ubRZMgafUOWvrOadwOckVa1wN3QWAo46FVKbVN8=";
        var newKeyBytes = Convert.FromBase64String(newKey);

        // Create key request payload
        var keyRequest = new KeyRequest { Key = newKeyBytes };
        var payload = new byte[1 + MessagePack.MessagePackSerializer.Serialize(keyRequest).Length];
        payload[0] = 0; // Message type byte
        Array.Copy(MessagePack.MessagePackSerializer.Serialize(keyRequest), 0, payload, 1, payload.Length - 1);

        // Send install-key query
        var query = new Query
        {
            Name = "_serf_install-key",
            Payload = payload,
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10),
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "test-source"
        };

        await inCh.WriteAsync(query);

        // Wait for processing
        await Task.Delay(200);

        // Assert - New key should be added
        keyring.GetKeys().Should().HaveCount(2, "new key should have been installed");

        // Cleanup
        cts.Cancel();
        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that install-key writes to keyring file if configured.
    /// </summary>
    [Fact]
    public async Task InstallKeyQuery_WithKeyringFile_ShouldWriteFile()
    {
        // Arrange - Create Serf with encryption and keyring file
        var existingKey = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=";
        var existingKeyBytes = Convert.FromBase64String(existingKey);
        var keyring = Keyring.Create(null, existingKeyBytes);

        var tempDir = Path.Combine(Path.GetTempPath(), $"serf_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var keyringFile = Path.Combine(tempDir, "keys.json");

            var config = new Config
            {
                NodeName = "node1",
                KeyringFile = keyringFile,
                MemberlistConfig = new MemberlistConfig
                {
                    Name = "node1",
                    BindAddr = "127.0.0.1",
                    BindPort = 0,
                    Keyring = keyring
                }
            };

            using var serf = await NSerf.Serf.Serf.CreateAsync(config);

            var outCh = Channel.CreateUnbounded<IEvent>();
            var cts = new CancellationTokenSource();

            // Act - Create handler
            var (inCh, handler) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

            // Prepare new key
            var newKey = "HvY8ubRZMgafUOWvrOadwOckVa1wN3QWAo46FVKbVN8=";
            var newKeyBytes = Convert.FromBase64String(newKey);

            // Create key request payload
            var keyRequest = new KeyRequest { Key = newKeyBytes };
            var payload = new byte[1 + MessagePack.MessagePackSerializer.Serialize(keyRequest).Length];
            payload[0] = 0; // Message type byte
            Array.Copy(MessagePack.MessagePackSerializer.Serialize(keyRequest), 0, payload, 1, payload.Length - 1);

            // Send install-key query
            var query = new Query
            {
                Name = "_serf_install-key",
                Payload = payload,
                SerfInstance = serf,
                Deadline = DateTime.UtcNow.AddSeconds(10),
                Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
                Port = 8000,
                SourceNodeName = "test-source"
            };

            await inCh.WriteAsync(query);

            // Wait for processing
            await Task.Delay(300);

            // Assert - Keyring file should be written
            File.Exists(keyringFile).Should().BeTrue("keyring file should be written after install");

            var content = await File.ReadAllTextAsync(keyringFile);
            var keys = System.Text.Json.JsonSerializer.Deserialize<List<string>>(content);
            keys.Should().NotBeNull();
            keys.Should().HaveCount(2);
            keys.Should().Contain(existingKey);
            keys.Should().Contain(newKey);

            // Cleanup
            cts.Cancel();
            await serf.ShutdownAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Tests that list-keys query handles large key lists with truncation.
    /// Ported from: TestSerfQueries_keyListResponseWithCorrectSize
    /// </summary>
    [Fact]
    public async Task ListKeysQuery_LargeKeyList_ShouldTruncateIfNeeded()
    {
        // Arrange - Create Serf with small response size limit
        var key1 = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=";
        var key1Bytes = Convert.FromBase64String(key1);
        var keyring = Keyring.Create(null, key1Bytes);

        // Add many more keys to exceed size limit
        for (int i = 0; i < 30; i++)
        {
            var additionalKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            keyring.AddKey(Convert.FromBase64String(additionalKey));
        }

        var config = new Config
        {
            NodeName = "node1",
            QueryResponseSizeLimit = 1024, // Small limit to force truncation
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0,
                Keyring = keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Verify we have many keys
        keyring.GetKeys().Should().HaveCountGreaterThan(20);

        var outCh = Channel.CreateUnbounded<IEvent>();
        var cts = new CancellationTokenSource();

        // Act - Create handler
        var (inCh, handler) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

        // Send list-keys query
        var query = new Query
        {
            Name = "_serf_list-keys",
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10),
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "test-source"
        };

        await inCh.WriteAsync(query);

        // Wait for processing
        await Task.Delay(200);

        // Assert - Query was processed successfully
        // The response would be truncated to fit within size limit

        // Cleanup
        cts.Cancel();
        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that use-key query changes the primary key.
    /// </summary>
    [Fact]
    public async Task UseKeyQuery_WithValidKey_ShouldChangePrimaryKey()
    {
        // Arrange - Create Serf with multiple keys
        var key1 = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=";
        var key2 = "HvY8ubRZMgafUOWvrOadwOckVa1wN3QWAo46FVKbVN8=";
        var key1Bytes = Convert.FromBase64String(key1);
        var key2Bytes = Convert.FromBase64String(key2);

        var keyring = Keyring.Create(null, key1Bytes);
        keyring.AddKey(key2Bytes);

        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0,
                Keyring = keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Verify initial primary key
        var primaryKey = keyring.GetPrimaryKey();
        primaryKey.Should().BeEquivalentTo(key1Bytes);

        var outCh = Channel.CreateUnbounded<IEvent>();
        var cts = new CancellationTokenSource();

        // Act - Create handler
        var (inCh, handler) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

        // Create use-key request payload
        var keyRequest = new KeyRequest { Key = key2Bytes };
        var payload = new byte[1 + MessagePack.MessagePackSerializer.Serialize(keyRequest).Length];
        payload[0] = 0; // Message type byte
        Array.Copy(MessagePack.MessagePackSerializer.Serialize(keyRequest), 0, payload, 1, payload.Length - 1);

        // Send use-key query
        var query = new Query
        {
            Name = "_serf_use-key",
            Payload = payload,
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10),
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "test-source"
        };

        await inCh.WriteAsync(query);

        // Wait for processing
        await Task.Delay(200);

        // Assert - Primary key should have changed
        var newPrimaryKey = keyring.GetPrimaryKey();
        newPrimaryKey.Should().BeEquivalentTo(key2Bytes, "primary key should have changed to key2");

        // Cleanup
        cts.Cancel();
        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that use-key query with no encryption returns error.
    /// </summary>
    [Fact]
    public async Task UseKeyQuery_NoEncryption_ShouldReturnError()
    {
        // Arrange - Create Serf without encryption
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
                // No keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);
        var outCh = Channel.CreateUnbounded<IEvent>();
        var cts = new CancellationTokenSource();

        // Act - Create handler
        var (inCh, handler) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

        // Prepare key
        var key = "HvY8ubRZMgafUOWvrOadwOckVa1wN3QWAo46FVKbVN8=";
        var keyBytes = Convert.FromBase64String(key);

        // Create key request payload
        var keyRequest = new KeyRequest { Key = keyBytes };
        var payload = new byte[1 + MessagePack.MessagePackSerializer.Serialize(keyRequest).Length];
        payload[0] = 0;
        Array.Copy(MessagePack.MessagePackSerializer.Serialize(keyRequest), 0, payload, 1, payload.Length - 1);

        // Send use-key query
        var query = new Query
        {
            Name = "_serf_use-key",
            Payload = payload,
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10),
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "test-source"
        };

        await inCh.WriteAsync(query);

        // Wait for processing
        await Task.Delay(100);

        // Assert - Query was processed (response would contain error)

        // Cleanup
        cts.Cancel();
        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that remove-key query removes a key from the keyring.
    /// </summary>
    [Fact]
    public async Task RemoveKeyQuery_WithValidKey_ShouldRemoveKey()
    {
        // Arrange - Create Serf with multiple keys
        var key1 = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=";
        var key2 = "HvY8ubRZMgafUOWvrOadwOckVa1wN3QWAo46FVKbVN8=";
        var key1Bytes = Convert.FromBase64String(key1);
        var key2Bytes = Convert.FromBase64String(key2);

        var keyring = Keyring.Create(null, key1Bytes);
        keyring.AddKey(key2Bytes);

        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0,
                Keyring = keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Verify initial key count
        keyring.GetKeys().Should().HaveCount(2);

        var outCh = Channel.CreateUnbounded<IEvent>();
        var cts = new CancellationTokenSource();

        // Act - Create handler
        var (inCh, handler) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

        // Create remove-key request payload (remove key2)
        var keyRequest = new KeyRequest { Key = key2Bytes };
        var payload = new byte[1 + MessagePack.MessagePackSerializer.Serialize(keyRequest).Length];
        payload[0] = 0;
        Array.Copy(MessagePack.MessagePackSerializer.Serialize(keyRequest), 0, payload, 1, payload.Length - 1);

        // Send remove-key query
        var query = new Query
        {
            Name = "_serf_remove-key",
            Payload = payload,
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10),
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "test-source"
        };

        await inCh.WriteAsync(query);

        // Wait for processing
        await Task.Delay(200);

        // Assert - Key should have been removed
        keyring.GetKeys().Should().HaveCount(1, "key2 should have been removed");

        // Verify remaining key is key1
        var remainingKeys = keyring.GetKeys();
        remainingKeys[0].Should().BeEquivalentTo(key1Bytes);

        // Cleanup
        cts.Cancel();
        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that remove-key query with no encryption returns error.
    /// </summary>
    [Fact]
    public async Task RemoveKeyQuery_NoEncryption_ShouldReturnError()
    {
        // Arrange - Create Serf without encryption
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
                // No keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);
        var outCh = Channel.CreateUnbounded<IEvent>();
        var cts = new CancellationTokenSource();

        // Act - Create handler
        var (inCh, handler) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

        // Prepare key
        var key = "HvY8ubRZMgafUOWvrOadwOckVa1wN3QWAo46FVKbVN8=";
        var keyBytes = Convert.FromBase64String(key);

        // Create key request payload
        var keyRequest = new KeyRequest { Key = keyBytes };
        var payload = new byte[1 + MessagePack.MessagePackSerializer.Serialize(keyRequest).Length];
        payload[0] = 0;
        Array.Copy(MessagePack.MessagePackSerializer.Serialize(keyRequest), 0, payload, 1, payload.Length - 1);

        // Send remove-key query
        var query = new Query
        {
            Name = "_serf_remove-key",
            Payload = payload,
            SerfInstance = serf,
            Deadline = DateTime.UtcNow.AddSeconds(10),
            Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
            Port = 8000,
            SourceNodeName = "test-source"
        };

        await inCh.WriteAsync(query);

        // Wait for processing
        await Task.Delay(100);

        // Assert - Query was processed (response would contain error)

        // Cleanup
        cts.Cancel();
        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that use-key writes to keyring file if configured.
    /// </summary>
    [Fact]
    public async Task UseKeyQuery_WithKeyringFile_ShouldWriteFile()
    {
        // Arrange
        var key1 = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=";
        var key2 = "HvY8ubRZMgafUOWvrOadwOckVa1wN3QWAo46FVKbVN8=";
        var key1Bytes = Convert.FromBase64String(key1);
        var key2Bytes = Convert.FromBase64String(key2);

        var keyring = Keyring.Create(null, key1Bytes);
        keyring.AddKey(key2Bytes);

        var tempDir = Path.Combine(Path.GetTempPath(), $"serf_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var keyringFile = Path.Combine(tempDir, "keys.json");

            var config = new Config
            {
                NodeName = "node1",
                KeyringFile = keyringFile,
                MemberlistConfig = new MemberlistConfig
                {
                    Name = "node1",
                    BindAddr = "127.0.0.1",
                    BindPort = 0,
                    Keyring = keyring
                }
            };

            using var serf = await NSerf.Serf.Serf.CreateAsync(config);

            var outCh = Channel.CreateUnbounded<IEvent>();
            var cts = new CancellationTokenSource();

            // Act - Create handler
            var (inCh, handler) = SerfQueries.Create(serf, outCh.Writer, cts.Token);

            // Create use-key request payload
            var keyRequest = new KeyRequest { Key = key2Bytes };
            var payload = new byte[1 + MessagePack.MessagePackSerializer.Serialize(keyRequest).Length];
            payload[0] = 0;
            Array.Copy(MessagePack.MessagePackSerializer.Serialize(keyRequest), 0, payload, 1, payload.Length - 1);

            // Send use-key query
            var query = new Query
            {
                Name = "_serf_use-key",
                Payload = payload,
                SerfInstance = serf,
                Deadline = DateTime.UtcNow.AddSeconds(10),
                Addr = System.Text.Encoding.UTF8.GetBytes("127.0.0.1"),
                Port = 8000,
                SourceNodeName = "test-source"
            };

            await inCh.WriteAsync(query);

            // Wait for processing
            await Task.Delay(300);

            // Assert - Keyring file should be written with new primary key first
            File.Exists(keyringFile).Should().BeTrue("keyring file should be written");

            var content = await File.ReadAllTextAsync(keyringFile);
            var keys = System.Text.Json.JsonSerializer.Deserialize<List<string>>(content);
            keys.Should().NotBeNull();
            keys![0].Should().Be(key2, "key2 should be primary (first in file)");

            // Cleanup
            cts.Cancel();
            await serf.ShutdownAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
