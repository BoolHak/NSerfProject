// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Tests for encryption and keyring functionality
// Ported from: github.com/hashicorp/serf/serf/serf_test.go

using System.Text;
using FluentAssertions;
using NSerf.Memberlist;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for Serf encryption and keyring management.
/// </summary>
public class SerfEncryptionTest
{
    /// <summary>
    /// Tests that EncryptionEnabled() returns false when no keyring is configured.
    /// </summary>
    [Fact]
    public async Task EncryptionEnabled_NoKeyring_ShouldReturnFalse()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Act
        var encryptionEnabled = serf.EncryptionEnabled();

        // Assert
        encryptionEnabled.Should().BeFalse("no keyring is configured");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that EncryptionEnabled() returns true when a keyring is configured.
    /// </summary>
    [Fact]
    public async Task EncryptionEnabled_WithKeyring_ShouldReturnTrue()
    {
        // Arrange
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

        // Act
        var encryptionEnabled = serf.EncryptionEnabled();

        // Assert
        encryptionEnabled.Should().BeTrue("a keyring is configured");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests WriteKeyringFile functionality.
    /// Ported from: TestSerf_WriteKeyringFile in serf_test.go
    /// </summary>
    [Fact]
    public async Task WriteKeyringFile_ShouldPersistKeysToFile()
    {
        // Arrange
        var existingKey = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=";
        var newKey = "HvY8ubRZMgafUOWvrOadwOckVa1wN3QWAo46FVKbVN8=";

        var tempDir = Path.Combine(Path.GetTempPath(), $"serf_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var keyringFile = Path.Combine(tempDir, "keys.json");

            var existingKeyBytes = Convert.FromBase64String(existingKey);
            var keyring = Keyring.Create(null, existingKeyBytes);

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

            // Act - Install a new key
            var newKeyBytes = Convert.FromBase64String(newKey);
            keyring.AddKey(newKeyBytes);
            
            // Manually trigger write (since InstallKey via query is not yet implemented)
            await serf.WriteKeyringFileAsync();

            // Assert - Verify file was created
            File.Exists(keyringFile).Should().BeTrue("keyring file should be created");

            // Read and verify content
            var content = await File.ReadAllTextAsync(keyringFile);

            // Verify it's valid JSON array and contains both keys
            var keys = System.Text.Json.JsonSerializer.Deserialize<List<string>>(content);
            keys.Should().NotBeNull();
            keys.Should().HaveCount(2, "should have 2 keys");
            keys.Should().Contain(existingKey, "existing key should be in file");
            keys.Should().Contain(newKey, "new key should be in file");

            // Act - Change primary key
            keyring.UseKey(newKeyBytes);
            await serf.WriteKeyringFileAsync();

            // Assert - Verify file was updated
            var updatedContent = await File.ReadAllTextAsync(keyringFile);

            // Verify primary key is first (GetKeys returns primary first)
            var updatedKeys = System.Text.Json.JsonSerializer.Deserialize<List<string>>(updatedContent);
            updatedKeys.Should().NotBeNull();
            updatedKeys.Should().HaveCount(2, "should still have 2 keys");
            updatedKeys.Should().Contain(existingKey, "existing key should still be in file");
            updatedKeys.Should().Contain(newKey, "new key should still be in file");
            updatedKeys![0].Should().Be(newKey, "new key should be primary (first in array)");

            await serf.ShutdownAsync();
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Tests that WriteKeyringFile does nothing when KeyringFile is not configured.
    /// </summary>
    [Fact]
    public async Task WriteKeyringFile_NoPathConfigured_ShouldDoNothing()
    {
        // Arrange
        var existingKey = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=";
        var existingKeyBytes = Convert.FromBase64String(existingKey);
        var keyring = Keyring.Create(null, existingKeyBytes);

        var config = new Config
        {
            NodeName = "node1",
            KeyringFile = null, // No file path configured
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0,
                Keyring = keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Act - Should not throw
        await serf.WriteKeyringFileAsync();

        // Assert - Success (no exception)
        serf.EncryptionEnabled().Should().BeTrue();

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that WriteKeyringFile throws when no keyring is available.
    /// </summary>
    [Fact]
    public async Task WriteKeyringFile_NoKeyring_ShouldThrow()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"serf_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var keyringFile = Path.Combine(tempDir, "keys.json");

            var config = new Config
            {
                NodeName = "node1",
                KeyringFile = keyringFile, // File path configured
                MemberlistConfig = new MemberlistConfig
                {
                    Name = "node1",
                    BindAddr = "127.0.0.1",
                    BindPort = 0
                    // No keyring configured
                }
            };

            using var serf = await NSerf.Serf.Serf.CreateAsync(config);

            // Act & Assert
            var act = async () => await serf.WriteKeyringFileAsync();
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("No keyring available to write");

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
