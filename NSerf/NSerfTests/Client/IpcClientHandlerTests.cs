using MessagePack;
using NSerf.Client;
using System.Net.Sockets;
using Xunit;

namespace NSerfTests.Client;

public class IpcClientHandlerTests
{
    [Fact]
    public async Task SendAsync_WritesHeaderAndBody()
    {
        var stream = new MemoryStream();
        var client = new IpcClientHandler("test-client", stream);
        
        var header = new ResponseHeader { Seq = 1, Error = "" };
        var body = new JoinResponse { Num = 3 };
        
        await client.SendAsync(header, body, CancellationToken.None);
        
        stream.Position = 0;
        var readHeader = await MessagePackSerializer.DeserializeAsync<ResponseHeader>(stream);
        var readBody = await MessagePackSerializer.DeserializeAsync<JoinResponse>(stream);
        
        Assert.Equal(1ul, readHeader.Seq);
        Assert.Equal(3, readBody.Num);
    }
    
    [Fact]
    public async Task SendAsync_WithNullBody_WritesOnlyHeader()
    {
        var stream = new MemoryStream();
        var client = new IpcClientHandler("test-client", stream);
        
        var header = new ResponseHeader { Seq = 42, Error = "" };
        
        await client.SendAsync(header, null, CancellationToken.None);
        
        stream.Position = 0;
        var readHeader = await MessagePackSerializer.DeserializeAsync<ResponseHeader>(stream);
        
        Assert.Equal(42ul, readHeader.Seq);
        // Stream should be at end after reading header
        Assert.Equal(stream.Length, stream.Position);
    }
    
    [Fact]
    public async Task SendAsync_WithConcurrentCalls_Serializes()
    {
        var stream = new MemoryStream();
        var client = new IpcClientHandler("test-client", stream);
        
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var header = new ResponseHeader { Seq = (ulong)i, Error = "" };
            await client.SendAsync(header, null, CancellationToken.None);
        });
        
        await Task.WhenAll(tasks);
        
        // Verify all 10 headers were written without corruption
        stream.Position = 0;
        for (int i = 0; i < 10; i++)
        {
            var header = await MessagePackSerializer.DeserializeAsync<ResponseHeader>(stream);
            Assert.True(header.Seq < 10);
        }
    }
    
    [Fact]
    public void Name_ReturnsClientName()
    {
        var stream = new MemoryStream();
        var client = new IpcClientHandler("my-test-client", stream);
        
        Assert.Equal("my-test-client", client.Name);
    }
    
    [Fact]
    public void Version_DefaultsToZero()
    {
        var stream = new MemoryStream();
        var client = new IpcClientHandler("test", stream);
        
        Assert.Equal(0, client.Version);
    }
    
    [Fact]
    public void DidAuth_DefaultsToFalse()
    {
        var stream = new MemoryStream();
        var client = new IpcClientHandler("test", stream);
        
        Assert.False(client.DidAuth);
    }
    
    [Fact]
    public void Version_CanBeSet()
    {
        var stream = new MemoryStream();
        var client = new IpcClientHandler("test", stream);
        
        client.Version = 1;
        
        Assert.Equal(1, client.Version);
    }
    
    [Fact]
    public void DidAuth_CanBeSet()
    {
        var stream = new MemoryStream();
        var client = new IpcClientHandler("test", stream);
        
        client.DidAuth = true;
        
        Assert.True(client.DidAuth);
    }
}
