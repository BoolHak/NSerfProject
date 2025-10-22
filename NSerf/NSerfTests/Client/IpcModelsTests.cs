using MessagePack;
using NSerf.Client;
using System.Net;
using Xunit;

namespace NSerfTests.Client;

public class IpcModelsTests
{
    private readonly MessagePackSerializerOptions _options = MessagePackSerializerOptions.Standard;

    [Fact]
    public void RequestHeader_SerializesCorrectly()
    {
        var header = new RequestHeader
        {
            Command = "test-command",
            Seq = 42
        };

        var bytes = MessagePackSerializer.Serialize(header, _options);
        var result = MessagePackSerializer.Deserialize<RequestHeader>(bytes, _options);

        Assert.Equal("test-command", result.Command);
        Assert.Equal(42ul, result.Seq);
    }

    [Fact]
    public void ResponseHeader_SerializesCorrectly()
    {
        var header = new ResponseHeader
        {
            Seq = 123,
            Error = "test error"
        };

        var bytes = MessagePackSerializer.Serialize(header, _options);
        var result = MessagePackSerializer.Deserialize<ResponseHeader>(bytes, _options);

        Assert.Equal(123ul, result.Seq);
        Assert.Equal("test error", result.Error);
    }

    [Fact]
    public void IpcMember_WithAllFields_SerializesCorrectly()
    {
        var member = new IpcMember
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.100").GetAddressBytes(),
            Port = 7946,
            Tags = new Dictionary<string, string> { ["role"] = "web", ["dc"] = "us-east" },
            Status = "alive",
            ProtocolMin = 2,
            ProtocolMax = 5,
            ProtocolCur = 5,
            DelegateMin = 2,
            DelegateMax = 5,
            DelegateCur = 5
        };

        var bytes = MessagePackSerializer.Serialize(member, _options);
        var result = MessagePackSerializer.Deserialize<IpcMember>(bytes, _options);

        Assert.Equal("node1", result.Name);
        Assert.Equal(member.Addr, result.Addr);
        Assert.Equal(7946, result.Port);
        Assert.Equal("web", result.Tags["role"]);
        Assert.Equal("us-east", result.Tags["dc"]);
        Assert.Equal("alive", result.Status);
        Assert.Equal((byte)2, result.ProtocolMin);
        Assert.Equal((byte)5, result.ProtocolMax);
        Assert.Equal((byte)5, result.ProtocolCur);
        Assert.Equal((byte)2, result.DelegateMin);
        Assert.Equal((byte)5, result.DelegateMax);
        Assert.Equal((byte)5, result.DelegateCur);
    }

    [Fact]
    public void IpcMember_GetIPAddress_ReturnsCorrectIPAddress()
    {
        var ipAddress = IPAddress.Parse("10.0.0.5");
        var member = new IpcMember { Addr = ipAddress.GetAddressBytes() };

        var result = member.GetIPAddress();

        Assert.Equal(ipAddress, result);
    }

    [Fact]
    public void HandshakeRequest_SerializesCorrectly()
    {
        var request = new HandshakeRequest { Version = 1 };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<HandshakeRequest>(bytes, _options);

        Assert.Equal(1, result.Version);
    }

    [Fact]
    public void AuthRequest_SerializesCorrectly()
    {
        var request = new AuthRequest { AuthKey = "secret123" };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<AuthRequest>(bytes, _options);

        Assert.Equal("secret123", result.AuthKey);
    }

    [Fact]
    public void JoinRequest_SerializesCorrectly()
    {
        var request = new JoinRequest
        {
            Existing = new[] { "node1:7946", "node2:7946" },
            Replay = true
        };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<JoinRequest>(bytes, _options);

        Assert.Equal(request.Existing, result.Existing);
        Assert.True(result.Replay);
    }

    [Fact]
    public void JoinResponse_SerializesCorrectly()
    {
        var response = new JoinResponse { Num = 5 };

        var bytes = MessagePackSerializer.Serialize(response, _options);
        var result = MessagePackSerializer.Deserialize<JoinResponse>(bytes, _options);

        Assert.Equal(5, result.Num);
    }

    [Fact]
    public void MembersFilteredRequest_SerializesCorrectly()
    {
        var request = new MembersFilteredRequest
        {
            Tags = new Dictionary<string, string> { ["role"] = "web.*" },
            Status = "alive",
            Name = "node.*"
        };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<MembersFilteredRequest>(bytes, _options);

        Assert.NotNull(result.Tags);
        Assert.Equal("web.*", result.Tags["role"]);
        Assert.Equal("alive", result.Status);
        Assert.Equal("node.*", result.Name);
    }

    [Fact]
    public void MembersResponse_SerializesCorrectly()
    {
        var response = new MembersResponse
        {
            Members = new[]
            {
                new IpcMember { Name = "node1", Status = "alive" },
                new IpcMember { Name = "node2", Status = "alive" }
            }
        };

        var bytes = MessagePackSerializer.Serialize(response, _options);
        var result = MessagePackSerializer.Deserialize<MembersResponse>(bytes, _options);

        Assert.Equal(2, result.Members.Length);
        Assert.Equal("node1", result.Members[0].Name);
        Assert.Equal("node2", result.Members[1].Name);
    }

    [Fact]
    public void EventRequest_SerializesCorrectly()
    {
        var request = new EventRequest
        {
            Name = "deploy",
            Payload = new byte[] { 1, 2, 3, 4 },
            Coalesce = true
        };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<EventRequest>(bytes, _options);

        Assert.Equal("deploy", result.Name);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.Payload);
        Assert.True(result.Coalesce);
    }

    [Fact]
    public void ForceLeaveRequest_SerializesCorrectly()
    {
        var request = new ForceLeaveRequest
        {
            Node = "node1",
            Prune = true
        };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<ForceLeaveRequest>(bytes, _options);

        Assert.Equal("node1", result.Node);
        Assert.True(result.Prune);
    }

    [Fact]
    public void TagsRequest_SerializesCorrectly()
    {
        var request = new TagsRequest
        {
            Tags = new Dictionary<string, string> { ["version"] = "2.0" },
            DeleteTags = new[] { "old-tag" }
        };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<TagsRequest>(bytes, _options);

        Assert.NotNull(result.Tags);
        Assert.Equal("2.0", result.Tags["version"]);
        Assert.NotNull(result.DeleteTags);
        Assert.Equal(new[] { "old-tag" }, result.DeleteTags);
    }

    [Fact]
    public void KeyRequest_SerializesCorrectly()
    {
        var request = new KeyRequest { Key = "encryption-key-123" };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<KeyRequest>(bytes, _options);

        Assert.Equal("encryption-key-123", result.Key);
    }

    [Fact]
    public void KeyResponse_SerializesCorrectly()
    {
        var response = new KeyResponse
        {
            Messages = new Dictionary<string, string> { ["node1"] = "ok", ["node2"] = "ok" },
            Keys = new Dictionary<string, int> { ["key1"] = 2, ["key2"] = 1 },
            NumNodes = 3,
            NumErr = 0,
            NumResp = 3
        };

        var bytes = MessagePackSerializer.Serialize(response, _options);
        var result = MessagePackSerializer.Deserialize<KeyResponse>(bytes, _options);

        Assert.Equal(2, result.Messages.Count);
        Assert.Equal("ok", result.Messages["node1"]);
        Assert.Equal(2, result.Keys["key1"]);
        Assert.Equal(3, result.NumNodes);
        Assert.Equal(0, result.NumErr);
        Assert.Equal(3, result.NumResp);
    }

    [Fact]
    public void MonitorRequest_SerializesCorrectly()
    {
        var request = new MonitorRequest { LogLevel = "DEBUG" };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<MonitorRequest>(bytes, _options);

        Assert.Equal("DEBUG", result.LogLevel);
    }

    [Fact]
    public void StreamRequest_SerializesCorrectly()
    {
        var request = new StreamRequest { Type = "user,member-join" };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<StreamRequest>(bytes, _options);

        Assert.Equal("user,member-join", result.Type);
    }

    [Fact]
    public void StopRequest_SerializesCorrectly()
    {
        var request = new StopRequest { Stop = 42 };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<StopRequest>(bytes, _options);

        Assert.Equal(42ul, result.Stop);
    }

    [Fact]
    public void QueryRequest_SerializesCorrectly()
    {
        var request = new QueryRequest
        {
            FilterNodes = new[] { "node1", "node2" },
            FilterTags = new Dictionary<string, string> { ["role"] = "web" },
            RequestAck = true,
            RelayFactor = 2,
            Timeout = TimeSpan.FromSeconds(30).Ticks,
            Name = "deploy-query",
            Payload = new byte[] { 5, 6, 7 }
        };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<QueryRequest>(bytes, _options);

        Assert.Equal(request.FilterNodes, result.FilterNodes);
        Assert.Equal("web", result.FilterTags!["role"]);
        Assert.True(result.RequestAck);
        Assert.Equal((byte)2, result.RelayFactor);
        Assert.Equal(TimeSpan.FromSeconds(30).Ticks, result.Timeout);
        Assert.Equal("deploy-query", result.Name);
        Assert.Equal(new byte[] { 5, 6, 7 }, result.Payload);
    }

    [Fact]
    public void RespondRequest_SerializesCorrectly()
    {
        var request = new RespondRequest
        {
            ID = 999,
            Payload = new byte[] { 10, 20, 30 }
        };

        var bytes = MessagePackSerializer.Serialize(request, _options);
        var result = MessagePackSerializer.Deserialize<RespondRequest>(bytes, _options);

        Assert.Equal(999ul, result.ID);
        Assert.Equal(new byte[] { 10, 20, 30 }, result.Payload);
    }

    [Fact]
    public void QueryRecord_SerializesCorrectly()
    {
        var record = new QueryRecord
        {
            Type = "response",
            From = "node1",
            Payload = new byte[] { 1, 2 }
        };

        var bytes = MessagePackSerializer.Serialize(record, _options);
        var result = MessagePackSerializer.Deserialize<QueryRecord>(bytes, _options);

        Assert.Equal("response", result.Type);
        Assert.Equal("node1", result.From);
        Assert.Equal(new byte[] { 1, 2 }, result.Payload);
    }

    [Fact]
    public void NodeResponse_SerializesCorrectly()
    {
        var response = new NodeResponse
        {
            From = "node1",
            Payload = new byte[] { 3, 4, 5 }
        };

        var bytes = MessagePackSerializer.Serialize(response, _options);
        var result = MessagePackSerializer.Deserialize<NodeResponse>(bytes, _options);

        Assert.Equal("node1", result.From);
        Assert.Equal(new byte[] { 3, 4, 5 }, result.Payload);
    }

    [Fact]
    public void LogRecord_SerializesCorrectly()
    {
        var record = new LogRecord { Log = "[INFO] Test log message" };

        var bytes = MessagePackSerializer.Serialize(record, _options);
        var result = MessagePackSerializer.Deserialize<LogRecord>(bytes, _options);

        Assert.Equal("[INFO] Test log message", result.Log);
    }

    [Fact]
    public void UserEventRecord_SerializesCorrectly()
    {
        var record = new UserEventRecord
        {
            Event = "user",
            LTime = 100,
            Name = "deploy",
            Payload = new byte[] { 1 },
            Coalesce = true
        };

        var bytes = MessagePackSerializer.Serialize(record, _options);
        var result = MessagePackSerializer.Deserialize<UserEventRecord>(bytes, _options);

        Assert.Equal("user", result.Event);
        Assert.Equal(100ul, result.LTime);
        Assert.Equal("deploy", result.Name);
        Assert.Equal(new byte[] { 1 }, result.Payload);
        Assert.True(result.Coalesce);
    }

    [Fact]
    public void QueryEventRecord_SerializesCorrectly()
    {
        var record = new QueryEventRecord
        {
            Event = "query",
            ID = 42,
            LTime = 200,
            Name = "health-check",
            Payload = new byte[] { 2, 3 }
        };

        var bytes = MessagePackSerializer.Serialize(record, _options);
        var result = MessagePackSerializer.Deserialize<QueryEventRecord>(bytes, _options);

        Assert.Equal("query", result.Event);
        Assert.Equal(42ul, result.ID);
        Assert.Equal(200ul, result.LTime);
        Assert.Equal("health-check", result.Name);
        Assert.Equal(new byte[] { 2, 3 }, result.Payload);
    }

    [Fact]
    public void MemberEventRecord_SerializesCorrectly()
    {
        var record = new MemberEventRecord
        {
            Event = "member-join",
            Members = new[]
            {
                new IpcMember { Name = "node1", Status = "alive" }
            }
        };

        var bytes = MessagePackSerializer.Serialize(record, _options);
        var result = MessagePackSerializer.Deserialize<MemberEventRecord>(bytes, _options);

        Assert.Equal("member-join", result.Event);
        Assert.Single(result.Members);
        Assert.Equal("node1", result.Members[0].Name);
    }
}
