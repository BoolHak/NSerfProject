// Ported from: github.com/hashicorp/memberlist/security.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Security.Cryptography;

namespace NSerf.Memberlist.Security;

/// <summary>
/// Provides encryption and decryption utilities for memberlist messages using AES-GCM.
/// </summary>
public static class SecurityTools
{
    private const int VersionSize = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int BlockSize = 16; // AES block size

    /// <summary>
    /// Returns the maximum possible overhead of encryption by version.
    /// </summary>
    public static int EncryptOverhead(byte version)
    {
        return version switch
        {
            0 => 45, // Version: 1, Nonce: 12, Padding: 16, Tag: 16
            1 => 29, // Version: 1, Nonce: 12, Tag: 16
            _ => throw new ArgumentException("Unsupported version", nameof(version))
        };
    }

    /// <summary>
    /// Computes the buffer size needed for a message of a given length.
    /// </summary>
    public static int EncryptedLength(byte version, int messageLength)
    {
        if (version >= 1)
        {
            // Version 1: no padding
            return VersionSize + NonceSize + messageLength + TagSize;
        }

        // Version 0: PKCS7 padding
        var padding = BlockSize - (messageLength % BlockSize);
        return VersionSize + NonceSize + messageLength + padding + TagSize;
    }

    /// <summary>
    /// Encrypts a message with the given key using AES-GCM.
    /// </summary>
    /// <param name="version">Encryption version (0 or 1).</param>
    /// <param name="key">Encryption key (16, 24, or 32 bytes).</param>
    /// <param name="message">Message to encrypt.</param>
    /// <param name="additionalData">Additional authenticated data.</param>
    /// <returns>Encrypted payload including version, nonce, ciphertext, and tag.</returns>
    public static byte[] EncryptPayload(byte version, byte[] key, byte[] message, byte[] additionalData)
    {
        if (version > 1)
        {
            throw new ArgumentException("Unsupported encryption version", nameof(version));
        }

        using var aes = new AesGcm(key, TagSize);

        // Prepare an output buffer
        var output = new List<byte> {
            // Add version
            version };

        // Generate random nonce
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        output.AddRange(nonce);

        // Prepare a message for encryption
        byte[] plaintext;
        // Version 0: Apply PKCS7 padding
        plaintext = version == 0 ? Pkcs7Encode(message) :
            // Version 1: No padding
            message;

        // Encrypt
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        aes.Encrypt(nonce, plaintext, ciphertext, tag, additionalData);

        // Append ciphertext and tag
        output.AddRange(ciphertext);
        output.AddRange(tag);

        return [.. output];
    }

    /// <summary>
    /// Decrypts a message using the provided keys, trying each until one succeeds.
    /// </summary>
    /// <param name="keys">List of keys to try for decryption.</param>
    /// <param name="encryptedMessage">Encrypted payload.</param>
    /// <param name="additionalData">Additional authenticated data.</param>
    /// <returns>Decrypted plaintext.</returns>
    public static byte[] DecryptPayload(byte[][] keys, byte[] encryptedMessage, byte[] additionalData)
    {
        // Validate input
        if (encryptedMessage.Length == 0)
        {
            throw new ArgumentException("Cannot decrypt empty payload", nameof(encryptedMessage));
        }

        // Extract version
        var version = encryptedMessage[0];
        if (version > 1)
        {
            throw new ArgumentException($"Unsupported encryption version {version}", nameof(encryptedMessage));
        }

        // Validate length
        var minLength = EncryptedLength(version, 0);
        if (encryptedMessage.Length < minLength)
        {
            throw new ArgumentException($"Payload is too small to decrypt: {encryptedMessage.Length}", nameof(encryptedMessage));
        }

        // Extract nonce
        var nonce = encryptedMessage.AsSpan(VersionSize, NonceSize).ToArray();

        // Extract ciphertext and tag
        const int ciphertextStart = VersionSize + NonceSize;
        var ciphertextLength = encryptedMessage.Length - ciphertextStart - TagSize;
        var ciphertext = encryptedMessage.AsSpan(ciphertextStart, ciphertextLength).ToArray();
        var tag = encryptedMessage.AsSpan(ciphertextStart + ciphertextLength, TagSize).ToArray();

        // Try each key
        foreach (var key in keys)
        {
            try
            {
                using var aes = new AesGcm(key, TagSize);
                var plaintext = new byte[ciphertext.Length];

                aes.Decrypt(nonce, ciphertext, tag, plaintext, additionalData);

                // Remove padding for version 0
                return version == 0 ? Pkcs7Decode(plaintext) : plaintext;
            }
            catch (CryptographicException)
            {
                // Try the next key
            }
        }

        throw new CryptographicException("No installed keys could decrypt the message");
    }

    /// <summary>
    /// Applies PKCS7 padding to a byte array.
    /// </summary>
    private static byte[] Pkcs7Encode(byte[] data)
    {
        var padding = BlockSize - (data.Length % BlockSize);
        var result = new byte[data.Length + padding];
        Array.Copy(data, result, data.Length);

        // Fill padding bytes with the padding length
        for (var i = data.Length; i < result.Length; i++)
        {
            result[i] = (byte)padding;
        }

        return result;
    }

    /// <summary>
    /// Removes PKCS7 padding from a byte array.
    /// </summary>
    private static byte[] Pkcs7Decode(byte[] data)
    {
        if (data.Length == 0)
        {
            throw new ArgumentException("Cannot decode a PKCS7 buffer of zero length");
        }

        int padding = data[^1];
        var newLength = data.Length - padding;

        var result = new byte[newLength];
        Array.Copy(data, result, newLength);

        return result;
    }
}
