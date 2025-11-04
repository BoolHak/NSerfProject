// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Client;
using Xunit;

namespace NSerfTests.Client;

/// <summary>
/// Tests for RPC Client - Phase 1
/// </summary>
public class RpcClientTests
{
    /// <summary>
    /// Test 1.1.1 - Basic TCP connection to RPC server
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task Test_1_1_1_BasicTcpConnection()
    {
        // Arrange
        using var server = new TestRpcServer();
        server.Start();
        
        var config = new RpcConfig
        {
            Address = $"127.0.0.1:{server.Port}",
            Timeout = TimeSpan.FromSeconds(5)
        };

        // Act
        using var client = new RpcClient(config);
        await client.ConnectAsync();
        
        // Assert
        Assert.True(client.IsConnected);
    }
    
    /// <summary>
    /// Test 1.1.2 - Connection timeout handling
    /// </summary>
    [Fact]
    public async Task Test_1_1_2_ConnectionTimeout()
    {
        // Arrange - Use invalid address that will timeout
        var config = new RpcConfig
        {
            Address = "192.0.2.1:7373", // Non-routable IP (RFC 5737)
            Timeout = TimeSpan.FromMilliseconds(100) // Very short timeout
        };

        using var client = new RpcClient(config);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await client.ConnectAsync();
        });
        
        Assert.False(client.IsConnected, "Client should not be connected after timeout");
    }

    /// <summary>
    /// Test 1.1.3 - Connection failure with invalid address format
    /// </summary>
    [Fact]
    public async Task Test_1_1_3_ConnectionFailureInvalidAddress()
    {
        // Arrange - Use malformed address (no port)
        var config = new RpcConfig
        {
            Address = "invalid_address", // Invalid format - no colon separator
            Timeout = TimeSpan.FromSeconds(1)
        };

        using var client = new RpcClient(config);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await client.ConnectAsync();
        });

        Assert.Contains("Invalid address format", exception.Message);
        Assert.False(client.IsConnected, "Client should not be connected after invalid address");
    }

    /// <summary>
    /// Test 1.1.4 - Connection failure with invalid port
    /// </summary>
    [Fact]
    public async Task Test_1_1_4_ConnectionFailureInvalidPort()
    {
        // Arrange - Use invalid port number
        var config = new RpcConfig
        {
            Address = "127.0.0.1:invalid", // Invalid port - not a number
            Timeout = TimeSpan.FromSeconds(1)
        };

        using var client = new RpcClient(config);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await client.ConnectAsync();
        });

        Assert.Contains("Invalid port", exception.Message);
        Assert.False(client.IsConnected);
    }

    /// <summary>
    /// Test 1.1.5 - Graceful connection close
    /// </summary>
    [Fact]
    public void Test_1_1_5_GracefulConnectionClose()
    {
        // Arrange
        var config = new RpcConfig
        {
            Address = "127.0.0.1:7373",
            Timeout = TimeSpan.FromSeconds(1)
        };

        var client = new RpcClient(config);

        // Act - Dispose without connecting (should not throw)
        client.Dispose();

        // Assert - Should be able to dispose safely
        Assert.False(client.IsConnected);

        // Verify double dispose doesn't throw
        client.Dispose();
        Assert.False(client.IsConnected);
    }

    // ========== 1.4 MessagePack Encoding Tests ==========

    /// <summary>
    /// Test 1.4.1 - Encode simple request header
    /// </summary>
    [Fact]
    public void Test_1_4_1_EncodeRequestHeader()
    {
        // Arrange
        var header = new RequestHeader
        {
            Command = "test-command",
            Seq = 12345
        };

        // Act
        var encoded = MessagePack.MessagePackSerializer.Serialize(header);

        // Assert
        Assert.NotNull(encoded);
        Assert.NotEmpty(encoded);

        // Verify we can decode it back
        var decoded = MessagePack.MessagePackSerializer.Deserialize<RequestHeader>(encoded);
        Assert.Equal("test-command", decoded.Command);
        Assert.Equal(12345ul, decoded.Seq);
    }

    /// <summary>
    /// Test 1.4.2 - Decode response header
    /// </summary>
    [Fact]
    public void Test_1_4_2_DecodeResponseHeader()
    {
        // Arrange
        var response = new ResponseHeader
        {
            Seq = 67890,
            Error = string.Empty
        };

        // Act
        var encoded = MessagePack.MessagePackSerializer.Serialize(response);
        var decoded = MessagePack.MessagePackSerializer.Deserialize<ResponseHeader>(encoded);

        // Assert
        Assert.Equal(67890ul, decoded.Seq);
        Assert.Equal(string.Empty, decoded.Error);
    }

    /// <summary>
    /// Test 1.4.3 - Encode handshake request
    /// </summary>
    [Fact]
    public void Test_1_4_3_EncodeHandshakeRequest()
    {
        // Arrange
        var request = new HandshakeRequest
        {
            Version = RpcConstants.MaxIpcVersion
        };

        // Act
        var encoded = MessagePack.MessagePackSerializer.Serialize(request);
        var decoded = MessagePack.MessagePackSerializer.Deserialize<HandshakeRequest>(encoded);

        // Assert
        Assert.Equal(RpcConstants.MaxIpcVersion, decoded.Version);
    }

    /// <summary>
    /// Test 1.4.4 - Encode authentication request
    /// </summary>
    [Fact]
    public void Test_1_4_4_EncodeAuthRequest()
    {
        // Arrange
        var request = new AuthRequest
        {
            AuthKey = "test-auth-key-12345"
        };

        // Act
        var encoded = MessagePack.MessagePackSerializer.Serialize(request);
        var decoded = MessagePack.MessagePackSerializer.Deserialize<AuthRequest>(encoded);

        // Assert
        Assert.Equal("test-auth-key-12345", decoded.AuthKey);
    }

    // ========== 1.5 Error Handling Tests ==========

    /// <summary>
    /// Test 1.5.1 - Multiple connection attempts should fail gracefully
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task Test_1_5_1_MultipleConnectionAttempts()
    {
        using var server = new TestRpcServer();
        server.Start();
        
        var config = new RpcConfig { Address = $"127.0.0.1:{server.Port}", Timeout = TimeSpan.FromSeconds(1) };
        using var client = new RpcClient(config);

        await client.ConnectAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await client.ConnectAsync();
        });
    }

    /// <summary>
    /// Test 1.5.2 - Dispose during connection should not throw
    /// </summary>
    [Fact]
    public void Test_1_5_2_DisposeDuringConnection()
    {
        var config = new RpcConfig { Address = "192.0.2.1:7373", Timeout = TimeSpan.FromSeconds(5) };
        var client = new RpcClient(config);

        var connectTask = Task.Run(async () =>
        {
            try { await client.ConnectAsync(); }
            catch { /* Expected */ }
        });

        Thread.Sleep(10);
        client.Dispose(); // Should not deadlock

        Assert.False(client.IsConnected);
    }

    /// <summary>
    /// Test 1.5.3 - Using disposed client should throw
    /// </summary>
    [Fact]
    public async Task Test_1_5_3_UseDisposedClient()
    {
        var config = new RpcConfig { Address = "127.0.0.1:7373", Timeout = TimeSpan.FromSeconds(1) };
        var client = new RpcClient(config);
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await client.ConnectAsync();
        });
    }
}
