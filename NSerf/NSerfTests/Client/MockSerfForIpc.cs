using NSerf.Serf;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;

namespace NSerfTests.Client;

/// <summary>
/// Helper to create a real Serf instance for IPC command testing.
/// </summary>
internal static class MockSerfForIpc
{
    public static NSerf.Serf.Serf Create()
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
        return NSerf.Serf.Serf.CreateAsync(config).GetAwaiter().GetResult();
    }
}
