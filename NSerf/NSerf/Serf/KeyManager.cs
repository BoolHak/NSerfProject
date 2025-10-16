// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/keymanager.go

using System.Threading.Channels;
using MessagePack;

namespace NSerf.Serf;

/// <summary>
/// KeyManager encapsulates all functionality within Serf for handling
/// encryption keyring changes across a cluster.
/// </summary>
public class KeyManager
{
    private readonly Serf _serf;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Creates a new KeyManager for the given Serf instance.
    /// </summary>
    public KeyManager(Serf serf)
    {
        _serf = serf ?? throw new ArgumentNullException(nameof(serf));
    }

    /// <summary>
    /// InstallKey handles broadcasting a query to all members and gathering
    /// responses from each of them, returning a list of messages from each node
    /// and any applicable error conditions.
    /// </summary>
    public async Task<KeyResponse> InstallKey(string key)
    {
        return await InstallKeyWithOptions(key, null);
    }

    /// <summary>
    /// InstallKey with optional parameters.
    /// </summary>
    public async Task<KeyResponse> InstallKeyWithOptions(string key, KeyRequestOptions? opts)
    {
        await _lock.WaitAsync();
        try
        {
            return await HandleKeyRequest(key, "install-key", opts);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// UseKey handles broadcasting a primary key change to all members in the
    /// cluster, and gathering any response messages.
    /// </summary>
    public async Task<KeyResponse> UseKey(string key)
    {
        return await UseKeyWithOptions(key, null);
    }

    /// <summary>
    /// UseKey with optional parameters.
    /// </summary>
    public async Task<KeyResponse> UseKeyWithOptions(string key, KeyRequestOptions? opts)
    {
        await _lock.WaitAsync();
        try
        {
            return await HandleKeyRequest(key, "use-key", opts);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// RemoveKey handles broadcasting a key to the cluster for removal.
    /// </summary>
    public async Task<KeyResponse> RemoveKey(string key)
    {
        return await RemoveKeyWithOptions(key, null);
    }

    /// <summary>
    /// RemoveKey with optional parameters.
    /// </summary>
    public async Task<KeyResponse> RemoveKeyWithOptions(string key, KeyRequestOptions? opts)
    {
        await _lock.WaitAsync();
        try
        {
            return await HandleKeyRequest(key, "remove-key", opts);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// ListKeys is used to collect installed keys from members in a Serf cluster
    /// and return an aggregated list of all installed keys.
    /// </summary>
    public async Task<KeyResponse> ListKeys()
    {
        return await ListKeysWithOptions(null);
    }

    /// <summary>
    /// ListKeys with optional parameters.
    /// </summary>
    public async Task<KeyResponse> ListKeysWithOptions(KeyRequestOptions? opts)
    {
        await _lock.WaitAsync();
        try
        {
            return await HandleKeyRequest("", "list-keys", opts);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// HandleKeyRequest performs query broadcasting to all members for any type of
    /// key operation and manages gathering responses.
    /// </summary>
    private async Task<KeyResponse> HandleKeyRequest(string key, string query, KeyRequestOptions? opts)
    {
        var resp = new KeyResponse();

        // TODO: Phase 9 - Implement full query broadcasting when Query() is available
        // This will involve:
        // 1. Decoding the base64 key
        // 2. Creating internal query name (_serf_<query>)
        // 3. Encoding the key request message
        // 4. Calling _serf.Query() with parameters
        // 5. Streaming responses and populating KeyResponse
        // 6. Checking for errors and partial success

        await Task.CompletedTask; // Placeholder for async signature
        return resp;
    }

    /// <summary>
    /// StreamKeyResp takes care of reading responses from a channel and composing
    /// them into a KeyResponse. It will update a KeyResponse in place.
    /// </summary>
    private async Task StreamKeyResp(KeyResponse resp, ChannelReader<NodeResponse> channel)
    {
        // TODO: Phase 9 - Implement response streaming
        // This will read from the channel and:
        // 1. Decode each NodeResponse
        // 2. Update resp.NumResp counter
        // 3. Parse nodeKeyResponse from payload
        // 4. Track errors in resp.Messages and resp.NumErr
        // 5. Aggregate keys in resp.Keys and resp.PrimaryKeys
        // 6. Return early when resp.NumResp == resp.NumNodes

        await Task.CompletedTask; // Placeholder
    }
}

/// <summary>
/// KeyResponse is used to relay a query for a list of all keys in use.
/// </summary>
public class KeyResponse
{
    /// <summary>
    /// Map of node name to response message.
    /// </summary>
    public Dictionary<string, string> Messages { get; set; } = new();

    /// <summary>
    /// Total nodes memberlist knows of.
    /// </summary>
    public int NumNodes { get; set; }

    /// <summary>
    /// Total responses received.
    /// </summary>
    public int NumResp { get; set; }

    /// <summary>
    /// Total errors from request.
    /// </summary>
    public int NumErr { get; set; }

    /// <summary>
    /// Keys is a mapping of the base64-encoded value of the key bytes to the
    /// number of nodes that have the key installed.
    /// </summary>
    public Dictionary<string, int> Keys { get; set; } = new();

    /// <summary>
    /// PrimaryKeys is a mapping of the base64-encoded value of the primary
    /// key bytes to the number of nodes that have the key installed.
    /// </summary>
    public Dictionary<string, int> PrimaryKeys { get; set; } = new();
}

/// <summary>
/// KeyRequestOptions is used to contain optional parameters for a keyring operation.
/// </summary>
public class KeyRequestOptions
{
    /// <summary>
    /// RelayFactor is the number of duplicate query responses to send by relaying through
    /// other nodes, for redundancy.
    /// </summary>
    public byte RelayFactor { get; set; }
}

/// <summary>
/// Internal key request structure used in wire protocol.
/// </summary>
[MessagePackObject(AllowPrivate = true)]
internal class KeyRequest
{
    [Key(0)]
    public byte[] Key { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Internal node key response structure used in wire protocol.
/// </summary>
[MessagePackObject(AllowPrivate = true)]
internal class NodeKeyResponse
{
    /// <summary>
    /// Result indicates true/false if there were errors or not.
    /// </summary>
    [Key(0)]
    public bool Result { get; set; }

    /// <summary>
    /// Message contains error messages or other information.
    /// </summary>
    [Key(1)]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Keys is used in listing queries to relay a list of installed keys.
    /// </summary>
    [Key(2)]
    public List<string> Keys { get; set; } = new();

    /// <summary>
    /// PrimaryKey is used in listing queries to relay the primary key.
    /// </summary>
    [Key(3)]
    public string PrimaryKey { get; set; } = string.Empty;
}
