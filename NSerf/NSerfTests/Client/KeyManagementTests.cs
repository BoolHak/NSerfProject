using NSerf.Client;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;

namespace NSerfTests.Client;

/// <summary>
/// TDD tests for Key Management command handlers.
/// Phase 4: RED phase - write tests before implementation.
/// </summary>
public class KeyManagementTests : IAsyncDisposable
{
    private readonly List<AgentIpc> _servers = new();
    private readonly List<IpcClient> _clients = new();
    private readonly List<NSerf.Serf.Serf> _serfInstances = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }
        foreach (var serf in _serfInstances)
        {
            serf.Dispose();
        }
    }

    private AgentIpc CreateServer(string? authKey = null)
    {
        var nodeName = $"test-node-{Guid.NewGuid()}";
        var config = new Config
        {
            NodeName = nodeName,
            MemberlistConfig = new MemberlistConfig
            {
                Name = nodeName,
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };
        var serf = NSerf.Serf.Serf.CreateAsync(config).GetAwaiter().GetResult();
        _serfInstances.Add(serf);
        var server = new AgentIpc(serf, "127.0.0.1:0", authKey);
        _servers.Add(server);
        return server;
    }

    private IpcClient CreateClient()
    {
        var client = new IpcClient();
        _clients.Add(client);
        return client;
    }

    [Fact(Timeout = 20000)]
    public async Task InstallKey_ValidKey_ReturnsSuccessResponse()
    {
        // RED: Test that install-key command works with KeyManager
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        // Install a new encryption key
        var testKey = Convert.ToBase64String(new byte[32]); // 32-byte key
        var (header, response) = await client.InstallKeyAsync(testKey, 2, CancellationToken.None);

        Assert.Equal(2ul, header.Seq);
        Assert.Equal("", header.Error);
        Assert.NotNull(response);
        Assert.NotNull(response.Keys);
        Assert.NotNull(response.Messages);
    }

    [Fact(Timeout = 20000)]
    public async Task UseKey_ExistingKey_ReturnsSuccessResponse()
    {
        // RED: Test that use-key command works with KeyManager
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var testKey = Convert.ToBase64String(new byte[32]);

        // First install the key
        await client.InstallKeyAsync(testKey, 2, CancellationToken.None);

        // Then use it as primary
        var (header, response) = await client.UseKeyAsync(testKey, 3, CancellationToken.None);

        Assert.Equal(3ul, header.Seq);
        Assert.Equal("", header.Error);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 20000)]
    public async Task RemoveKey_ExistingKey_ReturnsSuccessResponse()
    {
        // RED: Test that remove-key command works with KeyManager
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var testKey = Convert.ToBase64String(new byte[32]);

        // Install a key first
        await client.InstallKeyAsync(testKey, 2, CancellationToken.None);

        // Then remove it
        var (header, response) = await client.RemoveKeyAsync(testKey, 3, CancellationToken.None);

        Assert.Equal(3ul, header.Seq);
        Assert.Equal("", header.Error);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 20000)]
    public async Task ListKeys_ReturnsCurrentKeyring()
    {
        // RED: Test that list-keys command returns keyring state
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var (header, response) = await client.ListKeysAsync(2, CancellationToken.None);

        Assert.Equal(2ul, header.Seq);
        Assert.Equal("", header.Error);
        Assert.NotNull(response);
        Assert.NotNull(response.Keys);
        Assert.NotNull(response.Messages);
        // Initially should have no keys or report no encryption
        Assert.True(response.NumNodes >= 0);
    }

    [Fact(Timeout = 20000)]
    public async Task InstallKey_InvalidKey_ReturnsError()
    {
        // RED: Test that invalid key format is rejected
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        // Try to install an invalid key (wrong length)
        var invalidKey = "not-a-valid-key";
        var (header, response) = await client.InstallKeyAsync(invalidKey, 2, CancellationToken.None);

        Assert.Equal(2ul, header.Seq);
        // Should return error for invalid key
        Assert.NotEqual("", header.Error);
    }
}
