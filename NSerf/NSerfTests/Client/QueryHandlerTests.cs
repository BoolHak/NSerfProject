using MessagePack;
using NSerf.Client;
using System.Threading.Channels;
using Xunit;

namespace NSerfTests.Client;

public class QueryHandlerTests
{
    private readonly MessagePackSerializerOptions _options = MessagePackSerializerOptions.Standard
        .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);

    [Fact(Timeout = 5000)]
    public async Task QueryHandler_InitializationSuccess_CompletesInitTask()
    {
        var handler = new QueryHandler(_options, 1, null, null, _ => { });
        var stream = new MemoryStream();
        var reader = new MessagePackStreamReader(stream);
        
        var header = new ResponseHeader { Seq = 1, Error = "" };
        await handler.HandleAsync(header, reader);
        
        var error = await handler.InitTask;
        Assert.Equal("", error);
    }

    [Fact(Timeout = 5000)]
    public async Task QueryHandler_InitializationError_ReturnsError()
    {
        var handler = new QueryHandler(_options, 1, null, null, _ => { });
        var stream = new MemoryStream();
        var reader = new MessagePackStreamReader(stream);
        
        var header = new ResponseHeader { Seq = 1, Error = "query timeout" };
        await handler.HandleAsync(header, reader);
        
        var error = await handler.InitTask;
        Assert.Equal("query timeout", error);
    }

    [Fact(Timeout = 5000)]
    public async Task QueryHandler_AckRecord_WritesToAckChannel()
    {
        var ackChannel = Channel.CreateUnbounded<string>();
        bool deregistered = false;
        var handler = new QueryHandler(_options, 2, ackChannel.Writer, null, _ => deregistered = true);
        
        // Initialize first
        var stream1 = new MemoryStream();
        var reader1 = new MessagePackStreamReader(stream1);
        var initHeader = new ResponseHeader { Seq = 2, Error = "" };
        await handler.HandleAsync(initHeader, reader1);
        await handler.InitTask;
        
        // Send ack record
        var stream2 = new MemoryStream();
        var ackRecord = new QueryRecord { Type = "ack", From = "node1" };
        await MessagePackSerializer.SerializeAsync(stream2, ackRecord, _options);
        stream2.Position = 0;
        
        var reader2 = new MessagePackStreamReader(stream2);
        await handler.HandleAsync(new ResponseHeader { Seq = 2 }, reader2);
        
        // Should receive ack
        var ack = await ackChannel.Reader.ReadAsync();
        Assert.Equal("node1", ack);
        Assert.False(deregistered);
    }

    [Fact(Timeout = 5000)]
    public async Task QueryHandler_ResponseRecord_WritesToRespChannel()
    {
        var respChannel = Channel.CreateUnbounded<NodeResponse>();
        bool deregistered = false;
        var handler = new QueryHandler(_options, 3, null, respChannel.Writer, _ => deregistered = true);
        
        // Initialize
        var stream1 = new MemoryStream();
        var reader1 = new MessagePackStreamReader(stream1);
        await handler.HandleAsync(new ResponseHeader { Seq = 3, Error = "" }, reader1);
        await handler.InitTask;
        
        // Send response record
        var stream2 = new MemoryStream();
        var respRecord = new QueryRecord 
        { 
            Type = "response", 
            From = "node2", 
            Payload = new byte[] { 1, 2, 3 } 
        };
        await MessagePackSerializer.SerializeAsync(stream2, respRecord, _options);
        stream2.Position = 0;
        
        var reader2 = new MessagePackStreamReader(stream2);
        await handler.HandleAsync(new ResponseHeader { Seq = 3 }, reader2);
        
        // Should receive response
        var resp = await respChannel.Reader.ReadAsync();
        Assert.Equal("node2", resp.From);
        Assert.Equal(new byte[] { 1, 2, 3 }, resp.Payload);
        Assert.False(deregistered);
    }

    [Fact(Timeout = 5000)]
    public async Task QueryHandler_DoneRecord_DeregistersHandler()
    {
        bool deregistered = false;
        var handler = new QueryHandler(_options, 4, null, null, _ => deregistered = true);
        
        // Initialize
        var stream1 = new MemoryStream();
        var reader1 = new MessagePackStreamReader(stream1);
        await handler.HandleAsync(new ResponseHeader { Seq = 4, Error = "" }, reader1);
        await handler.InitTask;
        
        // Send done record
        var stream2 = new MemoryStream();
        var doneRecord = new QueryRecord { Type = "done" };
        await MessagePackSerializer.SerializeAsync(stream2, doneRecord, _options);
        stream2.Position = 0;
        
        var reader2 = new MessagePackStreamReader(stream2);
        await handler.HandleAsync(new ResponseHeader { Seq = 4 }, reader2);
        
        // Should deregister
        Assert.True(deregistered);
    }

    [Fact(Timeout = 5000)]
    public async Task QueryHandler_MultipleAcksAndResponses_AllDelivered()
    {
        var ackChannel = Channel.CreateUnbounded<string>();
        var respChannel = Channel.CreateUnbounded<NodeResponse>();
        bool deregistered = false;
        var handler = new QueryHandler(_options, 5, ackChannel.Writer, respChannel.Writer, _ => deregistered = true);
        
        // Initialize
        var stream1 = new MemoryStream();
        var reader1 = new MessagePackStreamReader(stream1);
        await handler.HandleAsync(new ResponseHeader { Seq = 5, Error = "" }, reader1);
        await handler.InitTask;
        
        // Send 3 acks
        for (int i = 0; i < 3; i++)
        {
            var stream = new MemoryStream();
            var record = new QueryRecord { Type = "ack", From = $"node{i}" };
            await MessagePackSerializer.SerializeAsync(stream, record, _options);
            stream.Position = 0;
            await handler.HandleAsync(new ResponseHeader { Seq = 5 }, new MessagePackStreamReader(stream));
        }
        
        // Send 2 responses
        for (int i = 0; i < 2; i++)
        {
            var stream = new MemoryStream();
            var record = new QueryRecord { Type = "response", From = $"node{i}", Payload = new byte[] { (byte)i } };
            await MessagePackSerializer.SerializeAsync(stream, record, _options);
            stream.Position = 0;
            await handler.HandleAsync(new ResponseHeader { Seq = 5 }, new MessagePackStreamReader(stream));
        }
        
        // Send done
        var doneStream = new MemoryStream();
        await MessagePackSerializer.SerializeAsync(doneStream, new QueryRecord { Type = "done" }, _options);
        doneStream.Position = 0;
        await handler.HandleAsync(new ResponseHeader { Seq = 5 }, new MessagePackStreamReader(doneStream));
        
        // Verify all acks received
        for (int i = 0; i < 3; i++)
        {
            var ack = await ackChannel.Reader.ReadAsync();
            Assert.Equal($"node{i}", ack);
        }
        
        // Verify all responses received
        for (int i = 0; i < 2; i++)
        {
            var resp = await respChannel.Reader.ReadAsync();
            Assert.Equal($"node{i}", resp.From);
            Assert.Equal(new byte[] { (byte)i }, resp.Payload);
        }
        
        Assert.True(deregistered);
    }

    [Fact(Timeout = 5000)]
    public async Task QueryHandler_CleanupBeforeInit_SignalsStreamClosed()
    {
        var handler = new QueryHandler(_options, 6, null, null, _ => { });
        
        await handler.CleanupAsync();
        
        var error = await handler.InitTask;
        Assert.Equal("Stream closed", error);
    }

    [Fact(Timeout = 5000)]
    public async Task QueryHandler_CleanupAfterInit_ClosesChannels()
    {
        var ackChannel = Channel.CreateUnbounded<string>();
        var respChannel = Channel.CreateUnbounded<NodeResponse>();
        var handler = new QueryHandler(_options, 7, ackChannel.Writer, respChannel.Writer, _ => { });
        
        // Initialize
        var stream = new MemoryStream();
        var reader = new MessagePackStreamReader(stream);
        await handler.HandleAsync(new ResponseHeader { Seq = 7, Error = "" }, reader);
        await handler.InitTask;
        
        // Cleanup
        await handler.CleanupAsync();
        
        // Channels should be completed
        await Assert.ThrowsAsync<ChannelClosedException>(async () => 
            await ackChannel.Reader.ReadAsync());
        await Assert.ThrowsAsync<ChannelClosedException>(async () => 
            await respChannel.Reader.ReadAsync());
    }
}
