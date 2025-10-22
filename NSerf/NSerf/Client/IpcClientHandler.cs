using MessagePack;
using System.Net.Sockets;

namespace NSerf.Client;

/// <summary>
/// Handles a single IPC client connection on the server side.
/// Manages per-client state including version, authentication, and active streams.
/// </summary>
public class IpcClientHandler : IAsyncDisposable
{
    private readonly string _name;
    private readonly Stream _stream;
    private readonly TcpClient _tcpClient;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly MessagePackSerializerOptions _options;
    
    /// <summary>
    /// Gets the client identifier (typically remote endpoint).
    /// </summary>
    public string Name => _name;
    
    /// <summary>
    /// Gets or sets the IPC protocol version negotiated with this client.
    /// Zero means handshake not yet performed.
    /// </summary>
    public int Version { get; set; }
    
    /// <summary>
    /// Gets or sets whether this client has successfully authenticated.
    /// </summary>
    public bool DidAuth { get; set; }
    
    public IpcClientHandler(string name, TcpClient tcpClient, MessagePackSerializerOptions? options = null)
    {
        _name = name;
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
        _options = options ?? MessagePackSerializerOptions.Standard;
    }
    
    // Test-only constructor that accepts a custom stream (e.g., MemoryStream)
    internal IpcClientHandler(string name, Stream stream, MessagePackSerializerOptions? options = null)
    {
        _name = name;
        _tcpClient = new TcpClient(); // Dummy TcpClient for tests
        _stream = stream;
        _options = options ?? MessagePackSerializerOptions.Standard;
    }
    
    /// <summary>
    /// Gets the underlying stream for reading requests.
    /// Internal use only - for AgentIpc server.
    /// </summary>
    internal Stream GetStream() => _stream;
    
    /// <summary>
    /// Sends a response header and optional body to the client.
    /// Thread-safe - serializes concurrent sends using a write lock.
    /// </summary>
    public async Task SendAsync(ResponseHeader header, object? body, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[IpcClientHandler] SendAsync: Seq={header.Seq}, Error={header.Error}, HasBody={body != null}");
        Console.WriteLine($"[IpcClientHandler] Pre-check; Stream CanWrite={_stream.CanWrite}, CanRead={_stream.CanRead}");
        
        // Check if stream is still alive before trying to send
        if (!_stream.CanWrite)
        {
            Console.WriteLine($"[IpcClientHandler] Stream is not writable, aborting send");
            return;
        }
        
        await _writeLock.WaitAsync();
        Console.WriteLine($"[IpcClientHandler] WriteLock acquired");
        try
        {
            Console.WriteLine($"[IpcClientHandler] Serializing header to buffer...");
            var headerBytes = MessagePackSerializer.Serialize(header, _options);
            Console.WriteLine($"[IpcClientHandler] Header bytes: {headerBytes.Length}");
            _stream.Write(headerBytes, 0, headerBytes.Length);
            Console.WriteLine($"[IpcClientHandler] Header written");
            
            if (body != null)
            {
                Console.WriteLine($"[IpcClientHandler] Serializing body to buffer...");
                var bodyType = body.GetType();
                var bodyBytes = MessagePackSerializer.Serialize(bodyType, body, _options);
                Console.WriteLine($"[IpcClientHandler] Body bytes: {bodyBytes.Length}");
                _stream.Write(bodyBytes, 0, bodyBytes.Length);
                Console.WriteLine($"[IpcClientHandler] Body written");
            }
            
            Console.WriteLine($"[IpcClientHandler] Flushing stream...");
            _stream.Flush();
            Console.WriteLine($"[IpcClientHandler] Stream flushed, response sent!");
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _tcpClient.Dispose();
        _writeLock.Dispose();
    }
}
