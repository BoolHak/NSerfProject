using NSerf.Agent;
using NSerf.Serf;
using NSerf.Serf.Events;
using Xunit;
using Xunit.Abstractions;

namespace NSerfTests.Agent;

public class EventFlowDiagnosticTest
{
    private readonly ITestOutputHelper _output;

    public EventFlowDiagnosticTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DiagnoseEventFlow()
    {
        _output.WriteLine("=== STARTING DIAGNOSTIC TEST ===");
        
        // Create agent config
        var agentConfig = new AgentConfig
        {
            NodeName = $"test-node-{Guid.NewGuid()}",
            BindAddr = "127.0.0.1:0",
            RpcAddr = null
        };

        _output.WriteLine($"1. Creating agent with node name: {agentConfig.NodeName}");
        
        var agent = await SerfAgent.CreateAsync(agentConfig);
        _output.WriteLine("2. Agent created");
        
        var receivedEvents = new List<Event>();
        var handler = new DelegateEventHandler(evt =>
        {
            _output.WriteLine($"   HANDLER CALLED! Event type: {evt.EventType()}");
            lock (receivedEvents)
            {
                receivedEvents.Add(evt);
            }
        });
        
        agent.RegisterEventHandler(handler);
        _output.WriteLine("3. Handler registered");
        
        _output.WriteLine("4. Starting agent...");
        await agent.StartAsync();
        _output.WriteLine("5. Agent started");
        
        // Check if Serf has already emitted events
        var members = agent.Serf.Members();
        _output.WriteLine($"6. Serf has {members.Length} members");
        foreach (var m in members)
        {
            _output.WriteLine($"   - {m.Name}: {m.Status}");
        }
        
        _output.WriteLine("7. Waiting 2 seconds for events...");
        await Task.Delay(2000);
        
        lock (receivedEvents)
        {
            _output.WriteLine($"8. Received {receivedEvents.Count} events");
            foreach (var evt in receivedEvents)
            {
                _output.WriteLine($"   - {evt.EventType()}");
            }
        }
        
        await agent.DisposeAsync();
        _output.WriteLine("9. Agent disposed");
        
        // The assertion
        lock (receivedEvents)
        {
            _output.WriteLine($"=== FINAL: {receivedEvents.Count} events received ===");
            Assert.NotEmpty(receivedEvents);
        }
    }
}
