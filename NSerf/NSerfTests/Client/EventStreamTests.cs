using NSerf.Client;
using NSerf.Serf;
using NSerf.Serf.Events;
using Xunit;

namespace NSerfTests.Client;

/// <summary>
/// TDD tests for EventStream per-client filtering (Phase 16 - Task 2.1).
/// Tests written BEFORE implementation (RED phase).
/// All tests include timeouts to prevent hanging.
/// </summary>
public class EventStreamTests
{
    [Fact(Timeout = 5000)]
    public async Task UserEventFilter_OnlySendsUserEvents()
    {
        // Test: Filter "user" should only send user events
        var client = new MockIpcClientHandler("test-client");
        var cts = new CancellationTokenSource();
        var stream = new EventStream(client, 1, "user", cts.Token);
        
        // Member event (should be filtered)
        var memberEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member>()
        };
        
        var matched1 = stream.MatchesFilter(memberEvent);
        Assert.False(matched1);
        
        // User event (should pass)
        var userEvent = new UserEvent
        {
            LTime = 1,
            Name = "deploy",
            Payload = Array.Empty<byte>()
        };
        
        var matched2 = stream.MatchesFilter(userEvent);
        Assert.True(matched2);
        
        cts.Cancel();
        await Task.Delay(10);
    }

    [Fact(Timeout = 5000)]
    public async Task MemberJoinFilter_OnlySendsMemberJoins()
    {
        // Test: Filter "member-join" should only send member-join events
        var client = new MockIpcClientHandler("test-client");
        var cts = new CancellationTokenSource();
        var stream = new EventStream(client, 2, "member-join", cts.Token);
        
        // Member join (should pass)
        var joinEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member>()
        };
        Assert.True(stream.MatchesFilter(joinEvent));
        
        // Member leave (should be filtered)
        var leaveEvent = new MemberEvent
        {
            Type = EventType.MemberLeave,
            Members = new List<Member>()
        };
        Assert.False(stream.MatchesFilter(leaveEvent));
        
        cts.Cancel();
        await Task.Delay(10);
    }

    [Fact(Timeout = 5000)]
    public async Task WildcardFilter_SendsAllEvents()
    {
        // Test: Filter "*" should send all events
        var client = new MockIpcClientHandler("test-client");
        var cts = new CancellationTokenSource();
        var stream = new EventStream(client, 3, "*", cts.Token);
        
        var memberEvent = new MemberEvent { Type = EventType.MemberJoin, Members = new List<Member>() };
        var userEvent = new UserEvent { LTime = 1, Name = "test", Payload = Array.Empty<byte>() };
        var queryEvent = new Query { LTime = 1, Name = "ping", Payload = Array.Empty<byte>() };
        
        Assert.True(stream.MatchesFilter(memberEvent));
        Assert.True(stream.MatchesFilter(userEvent));
        Assert.True(stream.MatchesFilter(queryEvent));
        
        cts.Cancel();
        await Task.Delay(10);
    }

    [Fact(Timeout = 5000)]
    public async Task AllSevenEventTypes_FilterCorrectly()
    {
        // Test: All 7 event types filter correctly
        var client = new MockIpcClientHandler("test-client");
        var cts = new CancellationTokenSource();
        
        var events = new[]
        {
            (new MemberEvent { Type = EventType.MemberJoin, Members = new List<Member>() }, "member-join"),
            (new MemberEvent { Type = EventType.MemberLeave, Members = new List<Member>() }, "member-leave"),
            (new MemberEvent { Type = EventType.MemberFailed, Members = new List<Member>() }, "member-failed"),
            (new MemberEvent { Type = EventType.MemberUpdate, Members = new List<Member>() }, "member-update"),
            (new MemberEvent { Type = EventType.MemberReap, Members = new List<Member>() }, "member-reap"),
            ((Event)new UserEvent { LTime = 1, Name = "test", Payload = Array.Empty<byte>() }, "user"),
            ((Event)new Query { LTime = 1, Name = "ping", Payload = Array.Empty<byte>() }, "query")
        };
        
        foreach (var (evt, filterType) in events)
        {
            var stream = new EventStream(client, 4, filterType, cts.Token);
            Assert.True(stream.MatchesFilter(evt), $"Event {evt.GetType().Name} should match filter {filterType}");
            
            // Verify it doesn't match other types
            var otherEvent = new UserEvent { LTime = 1, Name = "other", Payload = Array.Empty<byte>() };
            if (filterType != "user")
            {
                Assert.False(stream.MatchesFilter(otherEvent), $"Filter {filterType} should not match user event");
            }
        }
        
        cts.Cancel();
        await Task.Delay(10);
    }

    [Fact(Timeout = 5000)]
    public async Task WireFormat_MatchesGoProtocol()
    {
        // Test: Event conversion should produce correct wire format
        var client = new MockIpcClientHandler("test-client");
        var cts = new CancellationTokenSource();
        var stream = new EventStream(client, 5, "*", cts.Token);
        
        var userEvent = new UserEvent
        {
            LTime = 123,
            Name = "deploy",
            Payload = new byte[] { 1, 2, 3 },
            Coalesce = true
        };
        
        var record = stream.ConvertEventToRecord(userEvent);
        
        // Verify record structure (matches Go protocol)
        Assert.NotNull(record);
        var dict = record as Dictionary<string, object>;
        Assert.NotNull(dict);
        Assert.Equal("user", dict["Event"]);
        Assert.Equal((LamportTime)123, dict["LTime"]);
        Assert.Equal("deploy", dict["Name"]);
        
        cts.Cancel();
        await Task.Delay(10);
    }
}
