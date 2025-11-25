using Xunit;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;

namespace NSerfTests.Serf;

public class SerfIsReadyTests
{
    [Fact]
    public async Task IsReady_AfterCreate_ShouldReturnTrue()
    {
        var config = new Config
        {
            NodeName = "test-isready-create",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-isready-create",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        Assert.True(serf.IsReady());

        await serf.ShutdownAsync();
    }

    [Fact]
    public async Task IsReady_AfterLeave_ShouldReturnFalse()
    {
        var config = new Config
        {
            NodeName = "test-isready-leave",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-isready-leave",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);
        Assert.True(serf.IsReady());

        await serf.LeaveAsync();

        Assert.False(serf.IsReady());

        await serf.ShutdownAsync();
    }

    [Fact]
    public async Task IsReady_AfterShutdown_ShouldReturnFalse()
    {
        var config = new Config
        {
            NodeName = "test-isready-shutdown",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-isready-shutdown",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        await using var serf = await NSerf.Serf.Serf.CreateAsync(config);
        Assert.True(serf.IsReady());

        await serf.ShutdownAsync();

        Assert.False(serf.IsReady());
    }

    [Fact]
    public async Task IsReady_DuringLeaving_ShouldReturnFalse()
    {
        var config = new Config
        {
            NodeName = "test-isready-leaving",
            LeavePropagateDelay = TimeSpan.FromSeconds(5),
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-isready-leaving",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        await using var serf = await NSerf.Serf.Serf.CreateAsync(config);
        Assert.True(serf.IsReady());

        var leaveTask = serf.LeaveAsync();
        await Task.Delay(100);

        Assert.Equal(SerfState.SerfLeaving, serf.State());
        Assert.False(serf.IsReady());

        await leaveTask;
        await serf.ShutdownAsync();
    }

    [Fact]
    public void IsReady_WithoutMemberlist_ShouldReturnFalse()
    {
        var config = new Config
        {
            NodeName = "test-isready-no-memberlist"
        };

        var serf = new NSerf.Serf.Serf(config);

        Assert.False(serf.IsReady());

        serf.Dispose();
    }

    [Fact]
    public async Task IsReady_AfterDispose_ShouldReturnFalse()
    {
        var config = new Config
        {
            NodeName = "test-isready-dispose",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-isready-dispose",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        Assert.True(serf.IsReady());

        await serf.DisposeAsync();

        Assert.False(serf.IsReady());
    }

    [Fact]
    public async Task IsReady_MultipleCallsAfterCreate_ShouldBeConsistent()
    {
        var config = new Config
        {
            NodeName = "test-isready-consistent",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-isready-consistent",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        await using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        Assert.True(serf.IsReady());
        Assert.True(serf.IsReady());
        Assert.True(serf.IsReady());

        await serf.ShutdownAsync();

        Assert.False(serf.IsReady());
        Assert.False(serf.IsReady());
    }

    [Fact]
    public async Task IsReady_ConcurrentCalls_ShouldBeThreadSafe()
    {
        var config = new Config
        {
            NodeName = "test-isready-concurrent",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-isready-concurrent",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        await using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => serf.IsReady()))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, Assert.True);

        await serf.ShutdownAsync();
    }

    [Fact]
    public async Task IsReady_StateTransitions_ShouldReflectCorrectly()
    {
        var config = new Config
        {
            NodeName = "test-isready-transitions",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-isready-transitions",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        await using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        Assert.Equal(SerfState.SerfAlive, serf.State());
        Assert.True(serf.IsReady());

        await serf.LeaveAsync();
        Assert.Equal(SerfState.SerfLeft, serf.State());
        Assert.False(serf.IsReady());

        await serf.ShutdownAsync();
        Assert.Equal(SerfState.SerfShutdown, serf.State());
        Assert.False(serf.IsReady());
    }
}
