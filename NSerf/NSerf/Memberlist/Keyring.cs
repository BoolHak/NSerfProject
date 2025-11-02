// Ported from: github.com/hashicorp/memberlist/keyring.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Container for encryption keys used by memberlist. Keys are ordered such that
/// the first key (index 0) is the primary key used for encrypting messages,
/// and is the first key tried during message decryption.
/// </summary>
public class Keyring
{
    private readonly object _lock = new();
    private List<byte[]> _keys = new();
    
    private Keyring()
    {
    }
    
    /// <summary>
    /// Constructs a new keyring for encryption keys.
    /// </summary>
    /// <param name="secondaryKeys">Optional additional keys for decryption.</param>
    /// <param name="primaryKey">The primary key used for encryption. Must be 16, 24, or 32 bytes.</param>
    /// <returns>A new Keyring instance.</returns>
    public static Keyring Create(byte[][]? secondaryKeys, byte[] primaryKey)
    {
        if (primaryKey == null || primaryKey.Length == 0)
        {
            throw new ArgumentException("Empty primary key not allowed", nameof(primaryKey));
        }
        
        var keyring = new Keyring();
        keyring.AddKey(primaryKey);
        
        if (secondaryKeys != null)
        {
            foreach (var key in secondaryKeys)
            {
                keyring.AddKey(key);
            }
        }
        
        return keyring;
    }
    
    /// <summary>
    /// Validates that a key is the correct size for AES encryption.
    /// Key should be either 16, 24, or 32 bytes for AES-128, AES-192, or AES-256.
    /// </summary>
    public static void ValidateKey(byte[] key)
    {
        if (key.Length != 16 && key.Length != 24 && key.Length != 32)
        {
            throw new ArgumentException("Key size must be 16, 24 or 32 bytes", nameof(key));
        }
    }
    
    /// <summary>
    /// Installs a new key on the ring. The key will be available for decryption.
    /// If the key already exists, this is a no-op.
    /// </summary>
    public void AddKey(byte[] key)
    {
        ValidateKey(key);
        
        lock (_lock)
        {
            // Check if key already exists
            foreach (var existingKey in _keys)
            {
                if (KeysEqual(existingKey, key))
                {
                    return; // Already installed
                }
            }
            
            // Add new key
            var primaryKey = GetPrimaryKeyInternal();
            _keys.Add(key);
            
            // If this is the first key, it becomes primary
            if (primaryKey == null)
            {
                InstallKeysInternal(_keys, key);
            }
        }
    }
    
    /// <summary>
    /// Changes the key used to encrypt messages. The key must already be in the keyring.
    /// </summary>
    public void UseKey(byte[] key)
    {
        lock (_lock)
        {
            // Check if key exists
            bool found = false;
            foreach (var existingKey in _keys)
            {
                if (KeysEqual(existingKey, key))
                {
                    found = true;
                    break;
                }
            }
            
            if (!found)
            {
                throw new InvalidOperationException("Requested key is not in the keyring");
            }
            
            InstallKeysInternal(_keys, key);
        }
    }
    
    /// <summary>
    /// Removes a key from the keyring. Cannot remove the primary key.
    /// </summary>
    public void RemoveKey(byte[] key)
    {
        lock (_lock)
        {
            if (_keys.Count > 0 && KeysEqual(key, _keys[0]))
            {
                throw new InvalidOperationException("Removing the primary key is not allowed");
            }
            
            for (int i = 0; i < _keys.Count; i++)
            {
                if (KeysEqual(key, _keys[i]))
                {
                    var newKeys = new List<byte[]>(_keys);
                    newKeys.RemoveAt(i);
                    _keys = newKeys;
                    return;
                }
            }
        }
    }
    
    /// <summary>
    /// Returns the current set of keys on the ring.
    /// </summary>
    public List<byte[]> GetKeys()
    {
        lock (_lock)
        {
            return new List<byte[]>(_keys);
        }
    }
    
    /// <summary>
    /// Returns the primary key (position 0) used for encrypting messages.
    /// </summary>
    public byte[]? GetPrimaryKey()
    {
        lock (_lock)
        {
            return GetPrimaryKeyInternal();
        }
    }
    
    private byte[]? GetPrimaryKeyInternal()
    {
        return _keys.Count > 0 ? _keys[0] : null;
    }
    
    private void InstallKeysInternal(List<byte[]> keys, byte[] primaryKey)
    {
        var newKeys = new List<byte[]> { primaryKey };
        
        foreach (var key in keys)
        {
            if (!KeysEqual(key, primaryKey))
            {
                newKeys.Add(key);
            }
        }
        
        _keys = newKeys;
    }
    
    private static bool KeysEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        
        return true;
    }
}
