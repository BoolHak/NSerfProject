using NSerf.Client;
using Xunit;

namespace NSerfTests.Client;

public class IpcProtocolTests
{
    [Fact]
    public void MinVersion_ShouldBe_One()
    {
        Assert.Equal(1, IpcProtocol.MinVersion);
        Assert.Equal(1, IpcProtocol.MaxVersion);
    }

    [Fact]
    public void AllCommands_ShouldBeUnique()
    {
        var commands = new[]
        {
            IpcProtocol.HandshakeCommand,
            IpcProtocol.AuthCommand,
            IpcProtocol.JoinCommand,
            IpcProtocol.LeaveCommand,
            IpcProtocol.ForceLeaveCommand,
            IpcProtocol.MembersCommand,
            IpcProtocol.MembersFilteredCommand,
            IpcProtocol.EventCommand,
            IpcProtocol.TagsCommand,
            IpcProtocol.StreamCommand,
            IpcProtocol.MonitorCommand,
            IpcProtocol.StopCommand,
            IpcProtocol.InstallKeyCommand,
            IpcProtocol.UseKeyCommand,
            IpcProtocol.RemoveKeyCommand,
            IpcProtocol.ListKeysCommand,
            IpcProtocol.QueryCommand,
            IpcProtocol.RespondCommand,
            IpcProtocol.StatsCommand,
            IpcProtocol.GetCoordinateCommand
        };

        Assert.Equal(20, commands.Length);
        Assert.Equal(20, commands.Distinct().Count());
    }

    [Fact]
    public void AllErrors_ShouldBeUnique()
    {
        var errors = new[]
        {
            IpcProtocol.UnsupportedCommand,
            IpcProtocol.UnsupportedIPCVersion,
            IpcProtocol.DuplicateHandshake,
            IpcProtocol.HandshakeRequired,
            IpcProtocol.MonitorExists,
            IpcProtocol.InvalidFilter,
            IpcProtocol.StreamExists,
            IpcProtocol.InvalidQueryID,
            IpcProtocol.AuthRequired,
            IpcProtocol.InvalidAuthToken
        };

        Assert.Equal(10, errors.Length);
        Assert.Equal(10, errors.Distinct().Count());
    }

    [Fact]
    public void QueryRecordTypes_ShouldBeValid()
    {
        Assert.Equal("ack", IpcProtocol.QueryRecordAck);
        Assert.Equal("response", IpcProtocol.QueryRecordResponse);
        Assert.Equal("done", IpcProtocol.QueryRecordDone);
    }

    [Fact]
    public void CommandNames_ShouldMatchGoImplementation()
    {
        // Verify exact string values match Go implementation
        Assert.Equal("handshake", IpcProtocol.HandshakeCommand);
        Assert.Equal("auth", IpcProtocol.AuthCommand);
        Assert.Equal("join", IpcProtocol.JoinCommand);
        Assert.Equal("leave", IpcProtocol.LeaveCommand);
        Assert.Equal("force-leave", IpcProtocol.ForceLeaveCommand);
        Assert.Equal("members", IpcProtocol.MembersCommand);
        Assert.Equal("members-filtered", IpcProtocol.MembersFilteredCommand);
        Assert.Equal("event", IpcProtocol.EventCommand);
        Assert.Equal("tags", IpcProtocol.TagsCommand);
        Assert.Equal("stream", IpcProtocol.StreamCommand);
        Assert.Equal("monitor", IpcProtocol.MonitorCommand);
        Assert.Equal("stop", IpcProtocol.StopCommand);
        Assert.Equal("install-key", IpcProtocol.InstallKeyCommand);
        Assert.Equal("use-key", IpcProtocol.UseKeyCommand);
        Assert.Equal("remove-key", IpcProtocol.RemoveKeyCommand);
        Assert.Equal("list-keys", IpcProtocol.ListKeysCommand);
        Assert.Equal("query", IpcProtocol.QueryCommand);
        Assert.Equal("respond", IpcProtocol.RespondCommand);
        Assert.Equal("stats", IpcProtocol.StatsCommand);
        Assert.Equal("get-coordinate", IpcProtocol.GetCoordinateCommand);
    }
}
