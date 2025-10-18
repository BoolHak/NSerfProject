// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Internal Query Handler - Intercepts and handles _serf_* queries
// Ported from: github.com/hashicorp/serf/serf/internal_query.go

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Extensions.Logging;
using NSerf.Serf.Events;

namespace NSerf.Serf;

/// <summary>
/// Constants for internal Serf queries that are handled internally
/// and not forwarded to the client application.
/// Maps to: Go's constants in internal_query.go
/// </summary>
public static class InternalQueryConstants
{
    /// <summary>
    /// Prefix for all internal Serf queries.
    /// Any query starting with this prefix is handled internally.
    /// </summary>
    public const string InternalQueryPrefix = "_serf_";

    /// <summary>
    /// Ping query - used to check for reachability.
    /// Full name: "_serf_ping"
    /// </summary>
    public const string PingQuery = "ping";

    /// <summary>
    /// Conflict query - used to resolve name conflicts.
    /// Full name: "_serf_conflict"
    /// </summary>
    public const string ConflictQuery = "conflict";

    /// <summary>
    /// Install-key query - used to install a new encryption key.
    /// Full name: "_serf_install-key"
    /// </summary>
    public const string InstallKeyQuery = "install-key";

    /// <summary>
    /// Use-key query - used to change the primary encryption key.
    /// Full name: "_serf_use-key"
    /// </summary>
    public const string UseKeyQuery = "use-key";

    /// <summary>
    /// Remove-key query - used to remove a key from the keyring.
    /// Full name: "_serf_remove-key"
    /// </summary>
    public const string RemoveKeyQuery = "remove-key";

    /// <summary>
    /// List-keys query - used to list all known keys in the cluster.
    /// Full name: "_serf_list-keys"
    /// </summary>
    public const string ListKeysQuery = "list-keys";

    /// <summary>
    /// Minimum encoded key length for estimating response sizes.
    /// Used to calculate max keys that can fit in a response.
    /// </summary>
    public const int MinEncodedKeyLength = 25;

    /// <summary>
    /// Generates the full internal query name with prefix.
    /// </summary>
    /// <param name="name">The query name without prefix (e.g., "ping")</param>
    /// <returns>The full internal query name (e.g., "_serf_ping")</returns>
    public static string InternalQueryName(string name) => InternalQueryPrefix + name;
}

// Note: NodeKeyResponse is defined in KeyManager.cs and is reused here

/// <summary>
/// Handles internal Serf queries that start with "_serf_".
/// These queries are processed internally and not forwarded to the application.
/// Maps to: Go's serfQueries struct in internal_query.go
/// </summary>
public class SerfQueries
{
    private readonly ChannelReader<Event> _inCh;
    private readonly ChannelWriter<Event>? _outCh;
    private readonly Serf _serf;
    private readonly CancellationToken _shutdownToken;
    private readonly ILogger? _logger;

    /// <summary>
    /// Creates a new SerfQueries handler and returns the input channel.
    /// Maps to: Go's newSerfQueries() function
    /// </summary>
    /// <param name="serf">The Serf instance</param>
    /// <param name="outCh">Output channel for non-internal events (can be null)</param>
    /// <param name="shutdownToken">Cancellation token for shutdown</param>
    /// <returns>Tuple with input channel writer and handler instance</returns>
    public static (ChannelWriter<Event> InputChannel, SerfQueries Handler) Create(
        Serf serf,
        ChannelWriter<Event>? outCh,
        CancellationToken shutdownToken)
    {
        // Create unbounded channel for event ingestion
        var channel = Channel.CreateUnbounded<Event>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var handler = new SerfQueries(
            channel.Reader,
            outCh,
            serf,
            shutdownToken);

        // Start the background stream processor
        _ = Task.Run(async () => await handler.StreamAsync(), shutdownToken);

        return (channel.Writer, handler);
    }

    /// <summary>
    /// Private constructor - use Create() factory method instead.
    /// </summary>
    private SerfQueries(
        ChannelReader<Event> inCh,
        ChannelWriter<Event>? outCh,
        Serf serf,
        CancellationToken shutdownToken)
    {
        _inCh = inCh;
        _outCh = outCh;
        _serf = serf;
        _shutdownToken = shutdownToken;
        _logger = serf.Logger;
    }

    /// <summary>
    /// Background task that processes the event stream.
    /// Internal queries are handled, others are forwarded.
    /// Maps to: Go's stream() method
    /// </summary>
    internal async Task StreamAsync()
    {
        try
        {
            await foreach (var evt in _inCh.ReadAllAsync(_shutdownToken))
            {
                // Check if this is an internal query
                if (evt is Query query && query.Name.StartsWith(InternalQueryConstants.InternalQueryPrefix))
                {
                    // Handle internal query in background (don't block the stream)
                    _ = Task.Run(() => HandleQueryAsync(query), _shutdownToken);
                }
                else if (_outCh != null)
                {
                    // Forward non-internal events to the application
                    await _outCh.WriteAsync(evt, _shutdownToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[InternalQueryHandler] Error in event stream");
        }
    }

    /// <summary>
    /// Handles an internal query by dispatching to the appropriate handler.
    /// Maps to: Go's handleQuery() method
    /// </summary>
    private async Task HandleQueryAsync(Query query)
    {
        try
        {
            // Extract query name after the prefix
            var queryName = query.Name.Substring(InternalQueryConstants.InternalQueryPrefix.Length);

            switch (queryName)
            {
                case InternalQueryConstants.PingQuery:
                    // Nothing to do for ping - the query ACK is sufficient
                    _logger?.LogDebug("[InternalQueryHandler] Received ping query");
                    break;

                case InternalQueryConstants.ConflictQuery:
                    await HandleConflictAsync(query);
                    break;

                case InternalQueryConstants.InstallKeyQuery:
                    await HandleInstallKeyAsync(query);
                    break;

                case InternalQueryConstants.UseKeyQuery:
                    await HandleUseKeyAsync(query);
                    break;

                case InternalQueryConstants.RemoveKeyQuery:
                    await HandleRemoveKeyAsync(query);
                    break;

                case InternalQueryConstants.ListKeysQuery:
                    await HandleListKeysAsync(query);
                    break;

                default:
                    _logger?.LogWarning("[InternalQueryHandler] Unhandled internal query: {QueryName}", queryName);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[InternalQueryHandler] Error handling query: {QueryName}", query.Name);
        }
    }

    /// <summary>
    /// Handles conflict resolution queries.
    /// The payload is a node name, and the response should be the address we believe that node is at.
    /// Maps to: Go's handleConflict() method
    /// </summary>
    private async Task HandleConflictAsync(Query query)
    {
        try
        {
            // The target node name is the payload
            var nodeName = System.Text.Encoding.UTF8.GetString(query.Payload);

            // Do not respond to queries about ourselves
            if (nodeName == _serf.Config.NodeName)
            {
                return;
            }

            _logger?.LogDebug("[InternalQueryHandler] Got conflict resolution query for '{NodeName}'", nodeName);

            // Look for the member info
            _serf.AcquireMemberLock();
            Member? member = null;
            try
            {
                if (_serf.MemberStates.TryGetValue(nodeName, out var memberInfo))
                {
                    member = memberInfo.Member;
                }
            }
            finally
            {
                _serf.ReleaseMemberLock();
            }

            // Encode the response (null if we don't know the node)
            var buf = member != null 
                ? _serf.EncodeMessage(MessageType.ConflictResponse, member)
                : Array.Empty<byte>();

            // Send the response
            await query.RespondAsync(buf);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[InternalQueryHandler] Failed to handle conflict query");
        }
    }

    /// <summary>
    /// Handles list-keys queries.
    /// Returns a list of all installed keys (base64 encoded).
    /// Maps to: Go's handleListKeys() method
    /// </summary>
    private async Task HandleListKeysAsync(Query query)
    {
        var response = new NodeKeyResponse { Result = false };

        try
        {
            if (!_serf.EncryptionEnabled())
            {
                response.Message = "Keyring is empty (encryption not enabled)";
                _logger?.LogError("[InternalQueryHandler] Keyring is empty (encryption not enabled)");
                goto SEND;
            }

            _logger?.LogInformation("[InternalQueryHandler] Received list-keys query");

            var keyring = _serf.Config.MemberlistConfig?.Keyring;
            if (keyring == null)
            {
                response.Message = "Keyring not available";
                goto SEND;
            }

            // Get all keys and encode them to base64
            foreach (var keyBytes in keyring.GetKeys())
            {
                var key = Convert.ToBase64String(keyBytes);
                response.Keys.Add(key);
            }

            // Get primary key
            var primaryKeyBytes = keyring.GetPrimaryKey();
            if (primaryKeyBytes != null)
            {
                response.PrimaryKey = Convert.ToBase64String(primaryKeyBytes);
            }

            response.Result = true;
        }
        catch (Exception ex)
        {
            response.Message = ex.Message;
            _logger?.LogError(ex, "[InternalQueryHandler] Failed to list keys");
        }

    SEND:
        await SendKeyResponseAsync(query, response);
    }

    /// <summary>
    /// Handles install-key queries.
    /// Installs a new encryption key onto the keyring.
    /// Maps to: Go's handleInstallKey() method
    /// </summary>
    private async Task HandleInstallKeyAsync(Query query)
    {
        var response = new NodeKeyResponse { Result = false };

        try
        {
            // Decode the key request (skip first byte which is message type)
            var req = MessagePackSerializer.Deserialize<KeyRequest>(query.Payload.AsMemory().Slice(1));

            if (!_serf.EncryptionEnabled())
            {
                response.Message = "No keyring to modify (encryption not enabled)";
                _logger?.LogError("[InternalQueryHandler] No keyring to modify (encryption not enabled)");
                goto SEND;
            }

            _logger?.LogInformation("[InternalQueryHandler] Received install-key query");

            var keyring = _serf.Config.MemberlistConfig?.Keyring;
            if (keyring == null)
            {
                response.Message = "Keyring not available";
                goto SEND;
            }

            // Install the key
            keyring.AddKey(req.Key);

            // Write keyring file if configured
            if (!string.IsNullOrEmpty(_serf.Config.KeyringFile))
            {
                await _serf.WriteKeyringFileAsync();
            }

            response.Result = true;
        }
        catch (Exception ex)
        {
            response.Message = ex.Message;
            _logger?.LogError(ex, "[InternalQueryHandler] Failed to install key");
        }

    SEND:
        await SendKeyResponseAsync(query, response);
    }

    /// <summary>
    /// Handles use-key queries (change primary key).
    /// Changes the primary encryption key to the specified key.
    /// Maps to: Go's handleUseKey() method
    /// </summary>
    private async Task HandleUseKeyAsync(Query query)
    {
        var response = new NodeKeyResponse { Result = false };

        try
        {
            // Decode the key request (skip first byte which is message type)
            var req = MessagePackSerializer.Deserialize<KeyRequest>(query.Payload.AsMemory().Slice(1));

            if (!_serf.EncryptionEnabled())
            {
                response.Message = "No keyring to modify (encryption not enabled)";
                _logger?.LogError("[InternalQueryHandler] No keyring to modify (encryption not enabled)");
                goto SEND;
            }

            _logger?.LogInformation("[InternalQueryHandler] Received use-key query");

            var keyring = _serf.Config.MemberlistConfig?.Keyring;
            if (keyring == null)
            {
                response.Message = "Keyring not available";
                goto SEND;
            }

            // Change primary key
            keyring.UseKey(req.Key);

            // Write keyring file if configured
            if (!string.IsNullOrEmpty(_serf.Config.KeyringFile))
            {
                await _serf.WriteKeyringFileAsync();
            }

            response.Result = true;
        }
        catch (Exception ex)
        {
            response.Message = ex.Message;
            _logger?.LogError(ex, "[InternalQueryHandler] Failed to change primary key");
        }

    SEND:
        await SendKeyResponseAsync(query, response);
    }

    /// <summary>
    /// Handles remove-key queries.
    /// Removes a key from the keyring (cannot remove primary key).
    /// Maps to: Go's handleRemoveKey() method
    /// </summary>
    private async Task HandleRemoveKeyAsync(Query query)
    {
        var response = new NodeKeyResponse { Result = false };

        try
        {
            // Decode the key request (skip first byte which is message type)
            var req = MessagePackSerializer.Deserialize<KeyRequest>(query.Payload.AsMemory().Slice(1));

            if (!_serf.EncryptionEnabled())
            {
                response.Message = "No keyring to modify (encryption not enabled)";
                _logger?.LogError("[InternalQueryHandler] No keyring to modify (encryption not enabled)");
                goto SEND;
            }

            _logger?.LogInformation("[InternalQueryHandler] Received remove-key query");

            var keyring = _serf.Config.MemberlistConfig?.Keyring;
            if (keyring == null)
            {
                response.Message = "Keyring not available";
                goto SEND;
            }

            // Remove the key
            keyring.RemoveKey(req.Key);

            // Write keyring file if configured
            if (!string.IsNullOrEmpty(_serf.Config.KeyringFile))
            {
                await _serf.WriteKeyringFileAsync();
            }

            response.Result = true;
        }
        catch (Exception ex)
        {
            response.Message = ex.Message;
            _logger?.LogError(ex, "[InternalQueryHandler] Failed to remove key");
        }

    SEND:
        await SendKeyResponseAsync(query, response);
    }

    /// <summary>
    /// Sends a key response, handling size limits for list-keys queries.
    /// Maps to: Go's sendKeyResponse() method
    /// </summary>
    private async Task SendKeyResponseAsync(Query query, NodeKeyResponse response)
    {
        try
        {
            // For list-keys queries, handle potential size issues
            if (query.Name == InternalQueryConstants.InternalQueryName(InternalQueryConstants.ListKeysQuery))
            {
                await SendKeyListResponseAsync(query, response);
            }
            else
            {
                // For other key queries, send directly
                var buf = MessagePackSerializer.Serialize(response);
                await query.RespondAsync(buf);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[InternalQueryHandler] Failed to send key response");
        }
    }

    /// <summary>
    /// Sends a list-keys response with size truncation if needed.
    /// Maps to: Go's keyListResponseWithCorrectSize() and sendKeyResponse() for list-keys
    /// </summary>
    private async Task SendKeyListResponseAsync(Query query, NodeKeyResponse response)
    {
        var maxListKeys = _serf.Config.QueryResponseSizeLimit / InternalQueryConstants.MinEncodedKeyLength;
        var actual = response.Keys.Count;

        // If we have fewer keys than the max, just send them all
        if (maxListKeys > actual)
        {
            maxListKeys = actual;
        }

        // Try to send with progressively fewer keys until it fits
        for (int i = maxListKeys; i >= 0; i--)
        {
            try
            {
                var buf = MessagePackSerializer.Serialize(response);

                // Check size limit (approximate check)
                if (buf.Length <= _serf.Config.QueryResponseSizeLimit)
                {
                    if (actual > i && i < actual)
                    {
                        _logger?.LogWarning("[InternalQueryHandler] Truncated key list response, showing first {Count} of {Total} keys", i, actual);
                    }
                    await query.RespondAsync(buf);
                    return;
                }

                // Truncate and try again
                response.Keys = response.Keys.Take(i).ToList();
                response.Message = $"Truncated key list response, showing first {i} of {actual} keys";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[InternalQueryHandler] Error encoding key list response");
                return;
            }
        }

        _logger?.LogError("[InternalQueryHandler] Failed to truncate key list response to fit in message");
    }
}
