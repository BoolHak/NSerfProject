using MessagePack;
using NSerf.Client;
using System.Threading.Channels;
using Xunit;

namespace NSerfTests.Client;

public class CallbackHandlerTests
{
    private readonly MessagePackSerializerOptions _options = MessagePackSerializerOptions.Standard
        .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);

    [Fact(Timeout = 5000)]
    public async Task CallbackHandler_ResponseWithoutBody_CompletesWithHeaderOnly()
    {
        // Test for commands that return no body (handshake, auth, event, leave, etc.)
        var handler = new CallbackHandler(_options);
        var stream = new MemoryStream();
        
        // Simulate response with no body (just header)
        var header = new ResponseHeader { Seq = 1, Error = "" };
        
        var reader = new MessagePackStreamReader(stream);
        await handler.HandleAsync(header, reader);
        
        var (resultHeader, resultBody) = await handler.Task;
        
        Assert.Equal(1ul, resultHeader.Seq);
        Assert.Equal("", resultHeader.Error);
        Assert.Null(resultBody); // No body for commands like handshake
    }

    [Fact(Timeout = 5000)]
    public async Task CallbackHandler_ResponseWithBody_ReadsAndReturnsBody()
    {
        // Test for commands that return a body (members, stats, join, etc.)
        var handler = new CallbackHandler(_options);
        var stream = new MemoryStream();
        
        // Simulate response with body
        var header = new ResponseHeader { Seq = 2, Error = "" };
        var bodyData = new MembersResponse 
        { 
            Members = new[] 
            { 
                new IpcMember { Name = "node1", Status = "alive" } 
            } 
        };
        
        // Write body to stream (this is what the background reader would do)
        await MessagePackSerializer.SerializeAsync(stream, bodyData, _options);
        stream.Position = 0;
        
        var reader = new MessagePackStreamReader(stream);
        await handler.HandleAsync(header, reader);
        
        var (resultHeader, resultBody) = await handler.Task;
        
        Assert.Equal(2ul, resultHeader.Seq);
        Assert.NotNull(resultBody);
        
        // Deserialize the body
        var members = MessagePackSerializer.Deserialize<MembersResponse>(resultBody, _options);
        Assert.Single(members.Members);
        Assert.Equal("node1", members.Members[0].Name);
    }

    [Fact(Timeout = 5000)]
    public async Task CallbackHandler_ResponseWithError_ReturnsHeaderWithoutReadingBody()
    {
        // When error is present, no body should be read (Go behavior)
        var handler = new CallbackHandler(_options);
        var stream = new MemoryStream();
        
        var header = new ResponseHeader { Seq = 3, Error = "handshake required" };
        
        var reader = new MessagePackStreamReader(stream);
        await handler.HandleAsync(header, reader);
        
        var (resultHeader, resultBody) = await handler.Task;
        
        Assert.Equal(3ul, resultHeader.Seq);
        Assert.Equal("handshake required", resultHeader.Error);
        Assert.Null(resultBody); // No body when error is present
    }

    [Fact(Timeout = 5000)]
    public async Task CallbackHandler_CleanupBeforeCompletion_CancelsTask()
    {
        var handler = new CallbackHandler(_options);
        
        await handler.CleanupAsync();
        
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await handler.Task);
    }
}
