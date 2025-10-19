// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/query.go

using System.Threading.Channels;

namespace NSerf.Serf;

/// <summary>
/// QueryResponse is returned for each new Query. It is used to collect
/// Ack's as well as responses and to provide those back to a client.
/// </summary>
public class QueryResponse
{
    private readonly Channel<string>? _ackCh;
    private readonly Channel<NodeResponse> _respCh;
    private readonly HashSet<string> _acks = new();
    private readonly HashSet<string> _responses = new();
    private readonly object _closeLock = new();
    private bool _closed;
    private readonly Serf? _serf; // For metrics emission

    /// <summary>
    /// Deadline is the query end time (start + query timeout).
    /// </summary>
    public DateTime Deadline { get; set; }

    /// <summary>
    /// Query ID.
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    /// Stores the LTime of the query.
    /// </summary>
    public LamportTime LTime { get; set; }

    /// <summary>
    /// Creates a new QueryResponse.
    /// </summary>
    /// <param name="capacity">Channel capacity</param>
    /// <param name="requestAck">Whether acks are requested</param>
    /// <param name="serf">Serf instance for metrics emission</param>
    public QueryResponse(int capacity, bool requestAck, Serf? serf = null)
    {
        _respCh = Channel.CreateBounded<NodeResponse>(capacity);
        _serf = serf;
        
        if (requestAck)
        {
            _ackCh = Channel.CreateBounded<string>(capacity);
        }
    }

    /// <summary>
    /// Close is used to close the query, which will close the underlying
    /// channels and prevent further deliveries.
    /// </summary>
    public void Close()
    {
        lock (_closeLock)
        {
            if (_closed) return;
            _closed = true;

            _ackCh?.Writer.Complete();
            _respCh.Writer.Complete();
        }
    }

    /// <summary>
    /// Finished returns if the query is finished running.
    /// </summary>
    public bool Finished()
    {
        lock (_closeLock)
        {
            return _closed || DateTime.UtcNow > Deadline;
        }
    }

    /// <summary>
    /// AckCh returns a channel that can be used to listen for acks.
    /// Channel will be closed when the query is finished. This is null
    /// if the query did not specify RequestAck.
    /// </summary>
    public ChannelReader<string>? AckCh => _ackCh?.Reader;

    /// <summary>
    /// ResponseCh returns a channel that can be used to listen for responses.
    /// Channel will be closed when the query is finished.
    /// </summary>
    public ChannelReader<NodeResponse> ResponseCh => _respCh.Reader;

    /// <summary>
    /// SendResponse sends a response on the response channel ensuring the channel is not closed.
    /// </summary>
    public async Task SendResponse(NodeResponse nr)
    {
        // Atomically check for duplicate and add if not duplicate (Go serf.go:1447-1450)
        bool shouldSend;
        lock (_responses)
        {
            shouldSend = !_responses.Contains(nr.From);
            if (shouldSend)
            {
                // Reserve immediately to prevent race condition
                _responses.Add(nr.From);
            }
        }
        
        if (!shouldSend)
        {
            // Emit duplicate response metric
            _serf?.Config.Metrics.IncrCounter(new[] { "serf", "query_duplicate_responses" }, 1, _serf.Config.MetricLabels);
            return;
        }
        
        lock (_closeLock)
        {
            if (_closed)
            {
                return;
            }
        }

        if (await _respCh.Writer.WaitToWriteAsync())
        {
            await _respCh.Writer.WriteAsync(nr);
            // Emit valid response metric (Go serf.go:1452)
            _serf?.Config.Metrics.IncrCounter(new[] { "serf", "query_responses" }, 1, _serf.Config.MetricLabels);
        }
    }

    /// <summary>
    /// SendAck sends an ack on the ack channel ensuring the channel is not closed.
    /// </summary>
    public async Task SendAck(string from)
    {
        if (_ackCh == null)
        {
            return;
        }

        // Atomically check for duplicate and add if not duplicate (Go serf.go:1435-1438)
        bool shouldSend;
        lock (_acks)
        {
            shouldSend = !_acks.Contains(from);
            if (shouldSend)
            {
                // Reserve immediately to prevent race condition
                _acks.Add(from);
            }
        }
        
        if (!shouldSend)
        {
            // Emit duplicate ack metric
            _serf?.Config.Metrics.IncrCounter(new[] { "serf", "query_duplicate_acks" }, 1, _serf.Config.MetricLabels);
            return;
        }

        lock (_closeLock)
        {
            if (_closed)
            {
                return;
            }
        }

        if (await _ackCh.Writer.WaitToWriteAsync())
        {
            await _ackCh.Writer.WriteAsync(from);
            // Emit valid ack metric (Go serf.go:1440)
            _serf?.Config.Metrics.IncrCounter(new[] { "serf", "query_acks" }, 1, _serf.Config.MetricLabels);
        }
    }
}

/// <summary>
/// NodeResponse is used to represent a single response from a node.
/// </summary>
public class NodeResponse
{
    /// <summary>
    /// The node that sent the response.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// The response payload.
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}
