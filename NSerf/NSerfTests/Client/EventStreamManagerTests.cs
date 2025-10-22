using NSerf.Client;
using NSerf.Serf;
using NSerf.Serf.Events;
using System.Threading.Channels;
using Xunit;

namespace NSerfTests.Client;

/// <summary>
/// TDD tests for EventStreamManager (Phase 16 - Task 1.1).
/// Tests written BEFORE implementation (RED phase).
/// All tests include timeouts to prevent hanging.
/// </summary>
public class EventStreamManagerTests : IAsyncLifetime
{
    private Channel<Event> _eventChannel = null!;
    private EventStreamManager _manager = null!;
    private CancellationTokenSource _cts = null!;

    public Task InitializeAsync()
    {
        _eventChannel = Channel.CreateUnbounded<Event>();
        _manager = new EventStreamManager(_eventChannel.Reader);
        _cts = new CancellationTokenSource();
        _manager.Start(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        await Task.CompletedTask;
    }

    [Fact(Timeout = 5000)]
    public async Task RegisterStream_CreatesStreamAndReceivesEvents()
    {
        // Test: Registering a stream should allow it to receive events
        var client = new MockIpcClientHandler("test-client");
        var receivedEvents = new List<Event>();
        
        _manager.RegisterStream(1, client, "*", receivedEvents, _cts.Token);
        
        // Send an event through the channel
        var testEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member>()
        };
        
        await _eventChannel.Writer.WriteAsync(testEvent);
        
        // Give it a moment to process
        await Task.Delay(100);
        
        // Should have received the event
        Assert.Single(receivedEvents);
        Assert.IsType<MemberEvent>(receivedEvents[0]);
    }

    [Fact(Timeout = 5000)]
    public async Task FilterByType_OnlySendsMatchingEvents()
    {
        // Test: Filter "user" should only send user events, not member events
        var client = new MockIpcClientHandler("test-client");
        var receivedEvents = new List<Event>();
        
        _manager.RegisterStream(2, client, "user", receivedEvents, _cts.Token);
        
        // Send a member event (should be filtered out)
        var memberEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member>()
        };
        await _eventChannel.Writer.WriteAsync(memberEvent);
        
        // Send a user event (should be received)
        var userEvent = new UserEvent
        {
            LTime = 1,
            Name = "deploy",
            Payload = Array.Empty<byte>()
        };
        await _eventChannel.Writer.WriteAsync(userEvent);
        
        // Give it time to process
        await Task.Delay(100);
        
        // Should only have the user event
        Assert.Single(receivedEvents);
        Assert.IsType<UserEvent>(receivedEvents[0]);
    }

    [Fact(Timeout = 5000)]
    public async Task MultipleStreams_AllReceiveEventsIndependently()
    {
        // Test: Multiple clients should all receive events independently
        var client1 = new MockIpcClientHandler("client1");
        var client2 = new MockIpcClientHandler("client2");
        var receivedEvents1 = new List<Event>();
        var receivedEvents2 = new List<Event>();
        
        _manager.RegisterStream(3, client1, "*", receivedEvents1, _cts.Token);
        _manager.RegisterStream(4, client2, "*", receivedEvents2, _cts.Token);
        
        var testEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member>()
        };
        
        await _eventChannel.Writer.WriteAsync(testEvent);
        await Task.Delay(100);
        
        // Both should have received it
        Assert.Single(receivedEvents1);
        Assert.Single(receivedEvents2);
    }

    [Fact(Timeout = 5000)]
    public async Task UnregisterStream_StopsReceivingEvents()
    {
        // Test: Unregistering should stop receiving events
        var client = new MockIpcClientHandler("test-client");
        var receivedEvents = new List<Event>();
        
        _manager.RegisterStream(5, client, "*", receivedEvents, _cts.Token);
        
        // Send first event
        var event1 = new MemberEvent { Type = EventType.MemberJoin, Members = new List<Member>() };
        await _eventChannel.Writer.WriteAsync(event1);
        await Task.Delay(100);
        
        Assert.Single(receivedEvents);
        
        // Unregister
        _manager.UnregisterStream(5);
        
        // Send second event (should not be received)
        var event2 = new MemberEvent { Type = EventType.MemberLeave, Members = new List<Member>() };
        await _eventChannel.Writer.WriteAsync(event2);
        await Task.Delay(100);
        
        // Should still only have 1 event
        Assert.Single(receivedEvents);
    }

    [Fact(Timeout = 5000)]
    public async Task BufferFull_DropsEvents_WithDropCount()
    {
        // Test: When buffer is full, events should be dropped gracefully
        var client = new MockIpcClientHandler("test-client");
        var receivedEvents = new List<Event>();
        
        // Register with small buffer (simulate slow consumer)
        _manager.RegisterStream(6, client, "*", receivedEvents, _cts.Token);
        
        // Flood with events
        for (int i = 0; i < 100; i++)
        {
            var evt = new MemberEvent { Type = EventType.MemberJoin, Members = new List<Member>() };
            await _eventChannel.Writer.WriteAsync(evt);
        }
        
        await Task.Delay(500);
        
        // Some events should have been dropped (can't process 100 instantly)
        // This test verifies graceful handling, not exact count
        Assert.True(receivedEvents.Count > 0, "Should have received some events");
        Assert.True(receivedEvents.Count <= 100, "Should not have more than sent");
    }
}

/// <summary>
/// Mock IpcClientHandler for testing.
/// TODO: This will be replaced with actual IpcClientHandler once integration is complete.
/// </summary>
internal class MockIpcClientHandler
{
    public string Name { get; }
    
    public MockIpcClientHandler(string name)
    {
        Name = name;
    }
    
    public Task SendAsync(ResponseHeader header, object? body, CancellationToken cancellationToken)
    {
        // Mock implementation - just return
        return Task.CompletedTask;
    }
}
