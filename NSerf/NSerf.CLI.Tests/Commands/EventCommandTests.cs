// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Integration")]
[Collection("Sequential")]
public class EventCommandTests
{
    [Fact(Timeout = 10000)]
    public async Task EventCommand_WithNameOnly_Succeeds()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(EventCommand.Create());

        var args = new[] { "event", "--rpc-addr", fixture.RpcAddr!, "test-event" };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Event 'test-event' dispatched", output);
        Assert.Empty(error);
    }

    [Fact(Timeout = 10000)]
    public async Task EventCommand_WithPayload_Succeeds()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(EventCommand.Create());

        var args = new[] { "event", "--rpc-addr", fixture.RpcAddr!, "test-event", "test-payload" };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Event 'test-event' dispatched", output);
    }

    [Fact(Timeout = 10000)]
    public async Task EventCommand_WithCoalesceFalse_Succeeds()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(EventCommand.Create());

        var args = new[] { "event", "--rpc-addr", fixture.RpcAddr!, "--coalesce=false", "test-event" };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Coalescing enabled: False", output);
    }
}
