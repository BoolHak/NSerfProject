// Ported from: github.com/hashicorp/memberlist/keyring.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist;

namespace NSerfTests.Memberlist;

public class KeyringTests
{
    [Fact]
    public void NewKeyring_WithNoPrimaryKey_ShouldFail()
    {
        // Act
        Action act = () => Keyring.Create([], []);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*primary key*");
    }

    [Fact]
    public void NewKeyring_WithOnlyPrimaryKey_ShouldSucceed()
    {
        // Arrange
        var primaryKey = new byte[16]; // AES-128

        // Act
        var keyring = Keyring.Create(null, primaryKey);

        // Assert
        keyring.GetKeys().Should().HaveCount(1);
        keyring.GetPrimaryKey().Should().BeEquivalentTo(primaryKey);
    }

    [Fact]
    public void NewKeyring_WithMultipleKeys_ShouldInstallAll()
    {
        // Arrange
        var key1 = new byte[16];
        var key2 = new byte[24];
        var key3 = new byte[32];
        key1[0] = 1;
        key2[0] = 2;
        key3[0] = 3;

        // Act
        var keyring = Keyring.Create(new[] { key2, key3 }, key1);

        // Assert
        keyring.GetKeys().Should().HaveCount(3);
        keyring.GetPrimaryKey().Should().BeEquivalentTo(key1);
    }

    [Theory]
    [InlineData(16)] // AES-128
    [InlineData(24)] // AES-192
    [InlineData(32)] // AES-256
    public void ValidateKey_WithValidSizes_ShouldSucceed(int keySize)
    {
        // Arrange
        var key = new byte[keySize];

        // Act
        Action act = () => Keyring.ValidateKey(key);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(64)]
    public void ValidateKey_WithInvalidSizes_ShouldFail(int keySize)
    {
        // Arrange
        var key = new byte[keySize];

        // Act
        Action act = () => Keyring.ValidateKey(key);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*16, 24 or 32 bytes*");
    }

    [Fact]
    public void AddKey_NewKey_ShouldAdd()
    {
        // Arrange
        var primaryKey = new byte[16];
        primaryKey[0] = 1;
        var keyring = Keyring.Create(null, primaryKey);

        var newKey = new byte[16];
        newKey[0] = 2;

        // Act
        keyring.AddKey(newKey);

        // Assert
        keyring.GetKeys().Should().HaveCount(2);
    }

    [Fact]
    public void AddKey_DuplicateKey_ShouldBeNoop()
    {
        // Arrange
        var key = new byte[16];
        var keyring = Keyring.Create(null, key);

        // Act
        keyring.AddKey(key);

        // Assert
        keyring.GetKeys().Should().HaveCount(1);
    }

    [Fact]
    public void UseKey_ExistingKey_ShouldMakePrimary()
    {
        // Arrange
        var key1 = new byte[16];
        var key2 = new byte[16];
        key1[0] = 1;
        key2[0] = 2;

        var keyring = Keyring.Create(new[] { key2 }, key1);

        // Act
        keyring.UseKey(key2);

        // Assert
        keyring.GetPrimaryKey().Should().BeEquivalentTo(key2);
    }

    [Fact]
    public void UseKey_NonExistentKey_ShouldFail()
    {
        // Arrange
        var key1 = new byte[16];
        var key2 = new byte[16];
        key1[0] = 1;
        key2[0] = 2;

        var keyring = Keyring.Create(null, key1);

        // Act
        Action act = () => keyring.UseKey(key2);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not in the keyring*");
    }

    [Fact]
    public void RemoveKey_SecondaryKey_ShouldRemove()
    {
        // Arrange
        var key1 = new byte[16];
        var key2 = new byte[16];
        key1[0] = 1;
        key2[0] = 2;

        var keyring = Keyring.Create(new[] { key2 }, key1);

        // Act
        keyring.RemoveKey(key2);

        // Assert
        keyring.GetKeys().Should().HaveCount(1);
        keyring.GetKeys()[0].Should().BeEquivalentTo(key1);
    }

    [Fact]
    public void RemoveKey_PrimaryKey_ShouldFail()
    {
        // Arrange
        var key = new byte[16];
        var keyring = Keyring.Create(null, key);

        // Act
        Action act = () => keyring.RemoveKey(key);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*primary key*");
    }

    [Fact]
    public void GetKeys_ShouldReturnAllKeys()
    {
        // Arrange
        var key1 = new byte[16];
        var key2 = new byte[24];
        key1[0] = 1;
        key2[0] = 2;

        var keyring = Keyring.Create(new[] { key2 }, key1);

        // Act
        var keys = keyring.GetKeys();

        // Assert
        keys.Should().HaveCount(2);
        keys[0].Should().BeEquivalentTo(key1, "primary key should be first");
        keys[1].Should().BeEquivalentTo(key2);
    }
}
