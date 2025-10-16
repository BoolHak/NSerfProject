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
    public QueryResponse(int capacity, bool requestAck)
    {
        _respCh = Channel.CreateBounded<NodeResponse>(capacity);
        
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
        lock (_closeLock)
        {
            if (_closed) return;
        }

        if (await _respCh.Writer.WaitToWriteAsync())
        {
            await _respCh.Writer.WriteAsync(nr);
            lock (_responses)
            {
                _responses.Add(nr.From);
            }
        }
    }

    /// <summary>
    /// SendAck sends an ack on the ack channel ensuring the channel is not closed.
    /// </summary>
    public async Task SendAck(string from)
    {
        if (_ackCh == null) return;

        lock (_closeLock)
        {
            if (_closed) return;
        }

        if (await _ackCh.Writer.WaitToWriteAsync())
        {
            await _ackCh.Writer.WriteAsync(from);
            lock (_acks)
            {
                _acks.Add(from);
            }
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
