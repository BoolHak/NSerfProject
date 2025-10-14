// Ported from: github.com/hashicorp/memberlist/security.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist;

namespace NSerfTests.Memberlist;

public class SecurityTests
{
    [Fact]
    public void EncryptDecrypt_Version1_ShouldRoundTrip()
    {
        // Arrange
        var key = new byte[16]; // AES-128
        for (int i = 0; i < key.Length; i++)
        {
            key[i] = (byte)i;
        }
        
        var message = "Hello, encrypted world!"u8.ToArray();
        var additionalData = Array.Empty<byte>();
        
        // Act - Encrypt
        var encrypted = Security.EncryptPayload(1, key, message, additionalData);
        
        // Act - Decrypt
        var decrypted = Security.DecryptPayload(new[] { key }, encrypted, additionalData);
        
        // Assert
        decrypted.Should().BeEquivalentTo(message);
    }
    
    [Fact]
    public void EncryptDecrypt_Version0WithPadding_ShouldRoundTrip()
    {
        // Arrange
        var key = new byte[24]; // AES-192
        for (int i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(i * 2);
        }
        
        var message = "Padded message"u8.ToArray();
        var additionalData = Array.Empty<byte>();
        
        // Act - Encrypt
        var encrypted = Security.EncryptPayload(0, key, message, additionalData);
        
        // Act - Decrypt
        var decrypted = Security.DecryptPayload(new[] { key }, encrypted, additionalData);
        
        // Assert
        decrypted.Should().BeEquivalentTo(message);
    }
    
    [Fact]
    public void EncryptPayload_WithAdditionalData_ShouldAuthenticate()
    {
        // Arrange
        var key = new byte[32]; // AES-256
        var message = "Secret message"u8.ToArray();
        var additionalData = "metadata"u8.ToArray();
        
        // Act - Encrypt
        var encrypted = Security.EncryptPayload(1, key, message, additionalData);
        
        // Act - Decrypt with correct additional data
        var decrypted = Security.DecryptPayload(new[] { key }, encrypted, additionalData);
        
        // Assert
        decrypted.Should().BeEquivalentTo(message);
    }
    
    [Fact]
    public void DecryptPayload_WithWrongAdditionalData_ShouldFail()
    {
        // Arrange
        var key = new byte[16];
        var message = "Secret"u8.ToArray();
        var correctData = "metadata"u8.ToArray();
        var wrongData = "wrong"u8.ToArray();
        
        // Act - Encrypt with correct data
        var encrypted = Security.EncryptPayload(1, key, message, correctData);
        
        // Act - Try to decrypt with wrong data
        Action act = () => Security.DecryptPayload(new[] { key }, encrypted, wrongData);
        
        // Assert
        act.Should().Throw<Exception>();
    }
    
    [Fact]
    public void DecryptPayload_WithWrongKey_ShouldFail()
    {
        // Arrange
        var correctKey = new byte[16];
        correctKey[0] = 1;
        var wrongKey = new byte[16];
        wrongKey[0] = 2;
        
        var message = "Secret"u8.ToArray();
        
        // Act - Encrypt with correct key
        var encrypted = Security.EncryptPayload(1, correctKey, message, Array.Empty<byte>());
        
        // Act - Try to decrypt with wrong key
        Action act = () => Security.DecryptPayload(new[] { wrongKey }, encrypted, Array.Empty<byte>());
        
        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("*could*decrypt*");
    }
    
    [Fact]
    public void DecryptPayload_WithMultipleKeys_ShouldTryAll()
    {
        // Arrange
        var key1 = new byte[16];
        var key2 = new byte[16];
        var key3 = new byte[16];
        key1[0] = 1;
        key2[0] = 2;
        key3[0] = 3;
        
        var message = "Multi-key test"u8.ToArray();
        
        // Encrypt with key3
        var encrypted = Security.EncryptPayload(1, key3, message, Array.Empty<byte>());
        
        // Act - Decrypt with multiple keys (key3 is last)
        var decrypted = Security.DecryptPayload(new[] { key1, key2, key3 }, encrypted, Array.Empty<byte>());
        
        // Assert
        decrypted.Should().BeEquivalentTo(message);
    }
    
    [Fact]
    public void EncryptedLength_Version1_ShouldCalculateCorrectly()
    {
        // Arrange
        var messageLen = 100;
        
        // Act
        var expectedLen = Security.EncryptedLength(1, messageLen);
        
        // Assert - Version 1: 1 (version) + 12 (nonce) + 100 (message) + 16 (tag)
        expectedLen.Should().Be(129);
    }
    
    [Fact]
    public void EncryptedLength_Version0WithPadding_ShouldCalculateCorrectly()
    {
        // Arrange
        var messageLen = 10;
        
        // Act
        var expectedLen = Security.EncryptedLength(0, messageLen);
        
        // Assert - Version 0 includes PKCS7 padding
        // 1 (version) + 12 (nonce) + 10 (message) + 6 (padding to 16-byte block) + 16 (tag)
        expectedLen.Should().Be(45);
    }
    
    [Fact]
    public void DecryptPayload_EmptyMessage_ShouldFail()
    {
        // Arrange
        var key = new byte[16];
        var emptyMessage = Array.Empty<byte>();
        
        // Act
        Action act = () => Security.DecryptPayload(new[] { key }, emptyMessage, Array.Empty<byte>());
        
        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("*empty*");
    }
    
    [Fact]
    public void DecryptPayload_TooShort_ShouldFail()
    {
        // Arrange
        var key = new byte[16];
        var tooShort = new byte[] { 1, 2, 3 }; // Way too short
        
        // Act
        Action act = () => Security.DecryptPayload(new[] { key }, tooShort, Array.Empty<byte>());
        
        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("*too small*");
    }
}
