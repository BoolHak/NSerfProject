// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Phase 9.6: Query System Implementation

using System.Text;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace NSerf.Serf;

public partial class Serf
{
    /// <summary>
    /// Query initiates a new query across the cluster.
    /// Protocol version 4 or higher is required.
    /// Maps to: Go's Query() method
    /// </summary>
    public async Task<QueryResponse> QueryAsync(string name, byte[] payload, QueryParam? parameters = null)
    {
        // Check that the latest protocol is in use
        if (Config.ProtocolVersion < 4)
        {
            throw new InvalidOperationException("Query requires protocol version 4 or higher");
        }

        // Provide default parameters if none given
        if (parameters == null)
        {
            parameters = DefaultQueryParams();
        }
        else if (parameters.Timeout == TimeSpan.Zero)
        {
            parameters.Timeout = DefaultQueryTimeout();
        }

        // Get the local node
        var local = Memberlist?.LocalNode;
        if (local == null)
        {
            throw new InvalidOperationException("Memberlist not initialized");
        }

        // Encode the filters
        var filters = parameters.EncodeFilters();

        // Setup the flags
        uint flags = 0;
        if (parameters.RequestAck)
        {
            flags |= (uint)QueryFlags.Ack;
        }

        // Create a message
        var q = new MessageQuery
        {
            LTime = QueryClock.Time(),
            ID = (uint)_queryRandom.Next(),
            Addr = Encoding.UTF8.GetBytes(local.Addr.ToString()),
            Port = local.Port,
            SourceNode = local.Name,
            Filters = filters,
            Flags = flags,
            RelayFactor = parameters.RelayFactor,
            Timeout = parameters.Timeout,
            Name = name,
            Payload = payload
        };

        // Encode the query
        var raw = EncodeMessage(MessageType.Query, q);

        // Check the size
        if (raw.Length > Config.QuerySizeLimit)
        {
            throw new InvalidOperationException(
                $"Query exceeds limit of {Config.QuerySizeLimit} bytes");
        }

        // Register QueryResponse to track acks and responses
        var resp = new QueryResponse(Memberlist?.NumMembers() ?? 1, parameters.RequestAck, this)
        {
            Deadline = DateTime.UtcNow.Add(parameters.Timeout),
            Id = q.ID,
            LTime = q.LTime
        };
        RegisterQueryResponse(parameters.Timeout, resp);

        // Process query locally
        HandleQuery(q);

        // Start broadcasting the query
        Logger?.LogInformation("[Serf/Query] Queuing query '{Name}' for broadcast ({Size} bytes)", name, raw.Length);
        QueryBroadcasts.QueueBytes(raw);

        return await Task.FromResult(resp);
    }

    /// <summary>
    /// Registers a query response and schedules cleanup after timeout.
    /// Maps to: Go's registerQueryResponse()
    /// </summary>
    private void RegisterQueryResponse(TimeSpan timeout, QueryResponse resp)
    {
        WithWriteLock(_queryLock, () =>
        {
            // Map the LTime to the QueryResponse
            _queryResponses[resp.LTime] = resp;
        });

        // Setup a timer to close the response and deregister after the timeout
        _ = Task.Delay(timeout).ContinueWith(_ =>
        {
            WithWriteLock(_queryLock, () =>
            {
                _queryResponses.Remove(resp.LTime);
                resp.Close();
            });
        });
    }

    /// <summary>
    /// HandleQuery is invoked when we receive a query from another node.
    /// Returns true if the query should be rebroadcast.
    /// Maps to: Go's handleQuery()
    /// </summary>
    internal bool HandleQuery(MessageQuery query)
    {
        // Witness a potentially newer time
        QueryClock.Witness(query.LTime);

        return WithWriteLock(_queryLock, () =>
        {
            // Ignore if it is before our minimum query time
            if (query.LTime < QueryMinTime)
            {
                return false;
            }

            // Check if this message is too old
            var curTime = QueryClock.Time();
            var bufferSize = (ulong)Config.QueryBuffer;
            if (curTime > bufferSize && query.LTime < (curTime - bufferSize))
            {
                Logger?.LogWarning(
                    "[Serf] Received old query {Name} from time {LTime} (current: {CurTime})",
                    query.Name, query.LTime, curTime);
                return false;
            }

            // Check if we've already seen this query
            if (QueryBuffer.TryGetValue(query.LTime, out var seen))
            {
                // Check for duplicate
                if (seen.QueryIDs.Contains(query.ID))
                {
                    // Already seen this query
                    return false;
                }
                seen.QueryIDs.Add(query.ID);
            }
            else
            {
                // Create new collection for this LTime
                seen = new QueryCollection { LTime = query.LTime };
                seen.QueryIDs.Add(query.ID);
                QueryBuffer[query.LTime] = seen;
            }

            // Emit metrics
            // Reference: Go serf.go:1348-1349
            Config.Metrics.IncrCounter(["serf", "queries"], 1, Config.MetricLabels);
            Config.Metrics.IncrCounter(["serf", "queries", query.Name], 1, Config.MetricLabels);

            // Check if we should process this query
            if (!ShouldProcessQuery(query.Filters))
            {
                Logger?.LogDebug("[Serf] Ignoring query {Name} due to filters", query.Name);
                return true; // Rebroadcast but don't process
            }

            // Send acknowledgement if requested (send directly to originator, not via broadcast)
            if ((query.Flags & (uint)QueryFlags.Ack) != 0)
            {
                var ack = new MessageQueryResponse
                {
                    LTime = query.LTime,
                    ID = query.ID,
                    From = Config.NodeName,
                    Flags = (uint)QueryFlags.Ack,
                    Payload = []
                };
                var raw = EncodeMessage(MessageType.QueryResponse, ack);

                // CRITICAL: Wrap QueryResponse in User message type for memberlist transport
                // (same way Query messages are wrapped when broadcast)
                var wrapped = new byte[1 + raw.Length];
                wrapped[0] = (byte)NSerf.Memberlist.Messages.MessageType.User;
                Array.Copy(raw, 0, wrapped, 1, raw.Length);

                // Send directly to the query originator (matching Go implementation)
                var addrStr = System.Text.Encoding.UTF8.GetString(query.Addr);
                var targetAddr = new Memberlist.Transport.Address
                {
                    Addr = $"{addrStr}:{query.Port}",
                    Name = query.SourceNode ?? string.Empty
                };

                // Queue async send to avoid blocking the lock
                // Use Task.Run to ensure it executes on thread pool
                _ = Task.Run(async () => await SendAckAsync(wrapped, targetAddr, query.Name));
            }

            // Emit query event through EventManager (flows to SerfQueries for internal queries)
            var evt = new Events.Query
            {
                LTime = query.LTime,
                Name = query.Name,
                Payload = query.Payload,
                Id = query.ID,
                Addr = query.Addr,
                Port = query.Port,
                SourceNodeName = query.SourceNode ?? string.Empty,
                Deadline = DateTime.UtcNow.Add(query.Timeout),
                RelayFactor = query.RelayFactor,
                SerfInstance = this // CRITICAL: Set Serf instance so RespondAsync() works
            };

            try
            {
                _eventManager?.EmitEvent(evt);
                Logger?.LogTrace("[Serf] Emitted Query: {Name} at LTime {LTime}", query.Name, query.LTime);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "[Serf] Failed to emit Query: {Name}", query.Name);
            }

            return true; // Rebroadcast this query
        });
    }

    /// <summary>
    /// HandleQueryResponse processes incoming query responses.
    /// Maps to: Go's handleQueryResponse()
    /// </summary>
    internal void HandleQueryResponse(MessageQueryResponse response)
    {
        WithReadLock(_queryLock, () =>
        {
            // Lookup the corresponding QueryResponse
            if (!_queryResponses.TryGetValue(response.LTime, out var query))
            {
                Logger?.LogDebug("[Serf] Received response for unknown query at LTime {LTime}", response.LTime);
                return;
            }

            // Verify ID matches
            if (query.Id != response.ID)
            {
                Logger?.LogWarning("[Serf] Query ID mismatch: expected {Expected}, got {Actual}",
                    query.Id, response.ID);
                return;
            }

            // Check if this is an acknowledgement
            if ((response.Flags & (uint)QueryFlags.Ack) != 0)
            {
                // Handle ack - use async method with Task.Run to ensure execution
                _ = Task.Run(async () => await query.SendAck(response.From));
                Logger?.LogTrace("[Serf] Received ack from {From} for query", response.From);
            }
            else
            {
                // Handle response - use async method with Task.Run to ensure execution
                var nr = new NodeResponse
                {
                    From = response.From,
                    Payload = response.Payload
                };
                _ = Task.Run(async () => await query.SendResponse(nr));
                Logger?.LogTrace("[Serf] Received response from {From} for query", response.From);
            }
        });
    }

    /// <summary>
    /// ShouldProcessQuery checks if the local node matches the query filters.
    /// Maps to: Go's shouldProcessQuery()
    /// </summary>
    internal bool ShouldProcessQuery(List<byte[]> filters)
    {
        foreach (var filter in filters)
        {
            if (filter.Length == 0)
                continue;

            var filterType = (FilterType)filter[0];
            var payload = filter[1..];

            switch (filterType)
            {
                case FilterType.Node:
                    // Decode node names
                    var nodes = MessagePackSerializer.Deserialize<string[]>(payload);
                    if (!nodes.Contains(Config.NodeName))
                    {
                        return false; // Our node not in the filter
                    }
                    break;

                case FilterType.Tag:
                    // Decode tag filter
                    var tagFilter = MessagePackSerializer.Deserialize<FilterTag>(payload);
                    if (!Config.Tags.TryGetValue(tagFilter.Tag, out var tagValue))
                    {
                        return false; // We don't have this tag
                    }

                    // Check if tag value matches the regex
                    try
                    {
                        var regex = new System.Text.RegularExpressions.Regex(tagFilter.Expr);
                        if (!regex.IsMatch(tagValue))
                        {
                            return false; // Tag value doesn't match
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger?.LogWarning(ex, "[Serf] Invalid regex in tag filter: {Expr}", tagFilter.Expr);
                        return false;
                    }
                    break;

                default:
                    Logger?.LogWarning("[Serf] Unknown filter type: {Type}", filterType);
                    return false;
            }
        }

        return true; // Passed all filters
    }

    /// <summary>
    /// Helper method to send query acks asynchronously without blocking.
    /// </summary>
    private async Task SendAckAsync(byte[] raw, Memberlist.Transport.Address targetAddr, string queryName)
    {
        try
        {
            await Memberlist!.SendToAddress(targetAddr, raw, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[Serf] Failed to send ack for query {Name}", queryName);
        }
    }
}
