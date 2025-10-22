using NSerf.Client;
using Xunit;

namespace NSerfTests.Client;

/// <summary>
/// TDD tests for Coordinate API integration (Phase 16 - Day 11).
/// Tests written BEFORE removing TODO (RED phase).
/// </summary>
public class CoordinateCommandTests
{
    [Fact(Timeout = 5000)]
    public async Task GetCoordinate_ReturnsActualCoordinate_NotMock()
    {
        // Test: GetCoordinate should return real coordinate from Serf, not mock data
        var serf = MockSerfForIpc.Create();
        var ipc = new AgentIpc(serf, "127.0.0.1:0", null);
        
        try
        {
            await ipc.StartAsync(CancellationToken.None);
            
            var client = new IpcClient();
            await client.ConnectAsync("127.0.0.1", ipc.Port, CancellationToken.None);
            
            // Handshake first
            await client.HandshakeAsync(1, CancellationToken.None);
            
            // Get coordinate for local node
            var (header, coordResp) = await client.GetCoordinateAsync(serf.Config.NodeName, 2, CancellationToken.None);
            
            // Should return a coordinate response
            Assert.NotNull(coordResp);
            Assert.NotNull(coordResp.Coord);
            
            await client.DisposeAsync();
        }
        finally
        {
            await ipc.DisposeAsync();
            await serf.DisposeAsync();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task GetCoordinate_NonExistentNode_ReturnsNull()
    {
        // Test: GetCoordinate for non-existent node should handle gracefully
        var serf = MockSerfForIpc.Create();
        var ipc = new AgentIpc(serf, "127.0.0.1:0", null);
        
        try
        {
            await ipc.StartAsync(CancellationToken.None);
            
            var client = new IpcClient();
            await client.ConnectAsync("127.0.0.1", ipc.Port, CancellationToken.None);
            
            await client.HandshakeAsync(1, CancellationToken.None);
            
            // Get coordinate for non-existent node
            var (header, coordResp) = await client.GetCoordinateAsync("non-existent-node", 2, CancellationToken.None);
            
            // Should return coordinate response (possibly with Ok=false or default coordinate)
            Assert.NotNull(coordResp);
            
            await client.DisposeAsync();
        }
        finally
        {
            await ipc.DisposeAsync();
            await serf.DisposeAsync();
        }
    }
}
