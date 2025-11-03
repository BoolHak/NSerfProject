// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;
using NSerf.Client;
using NSerf.Client.Responses;

namespace NSerf.Agent.RPC;

/// <summary>
/// QueryResponseStream handles streaming query acks and responses back to the RPC client.
/// Maps to: Go's queryResponseStream in ipc_query_response_stream.go
/// </summary>
internal class QueryResponseStream(
    SemaphoreSlim writeLock,
    Stream stream,
    ulong seq,
    Serf.QueryResponse queryResponse)
{
    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.None);

    /// <summary>
    /// Stream is a long-running routine that streams query results back to the client.
    /// Maps to: Go's Stream() method in ipc_query_response_stream.go
    /// </summary>
    public async Task StreamAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Setup timer for query deadline
            var remaining = queryResponse.Deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                await SendDoneAsync(cancellationToken);
                return;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(remaining);

            var ackCh = queryResponse.AckCh;
            var respCh = queryResponse.ResponseCh;

            try
            {
                while (!timeoutCts.Token.IsCancellationRequested)
                {
                    // Try to read from an ack channel
                    if (ackCh != null && ackCh.TryRead(out var ack))
                    {
                        await SendAckAsync(ack, timeoutCts.Token);
                        continue;
                    }

                    // Try to read from a response channel
                    if (respCh.TryRead(out var resp))
                    {
                        await SendResponseAsync(resp.From, resp.Payload, timeoutCts.Token);
                        continue;
                    }

                    // Small delay to avoid busy-waiting
                    await Task.Delay(10, timeoutCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout reached or cancellation requested
            }

            // Send done marker
            await SendDoneAsync(timeoutCts.Token);
        }
        catch (Exception)
        {
            // Swallow exceptions - client may have disconnected
        }
    }

    private async Task SendAckAsync(string from, CancellationToken cancellationToken)
    {
        var record = new QueryRecord
        {
            Type = QueryRecordType.Ack,
            From = from,
            Payload = []
        };

        await SendRecordAsync(record, cancellationToken);
    }

    private async Task SendResponseAsync(string from, byte[] payload, CancellationToken cancellationToken)
    {
        var record = new QueryRecord
        {
            Type = QueryRecordType.Response,
            From = from,
            Payload = payload
        };

        await SendRecordAsync(record, cancellationToken);
    }

    private async Task SendDoneAsync(CancellationToken cancellationToken)
    {
        var record = new QueryRecord
        {
            Type = QueryRecordType.Done,
            From = string.Empty,
            Payload = []
        };

        await SendRecordAsync(record, cancellationToken);
    }

    private async Task SendRecordAsync(QueryRecord record, CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            var header = new ResponseHeader { Seq = seq, Error = string.Empty };
            var headerBytes = MessagePackSerializer.Serialize(header, MsgPackOptions, cancellationToken);
            await stream.WriteAsync(headerBytes, cancellationToken);

            var recordBytes = MessagePackSerializer.Serialize(record, MsgPackOptions, cancellationToken);
            await stream.WriteAsync(recordBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }
}
