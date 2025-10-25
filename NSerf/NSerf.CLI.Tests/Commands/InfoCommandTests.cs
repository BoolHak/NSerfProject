// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Integration")]
[Collection("Sequential")]
public class InfoCommandTests
{
    [Fact]
    public async Task InfoCommand_ReturnsStats()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(InfoCommand.Create());

        var args = new[] { "info", "--rpc-addr", fixture.RpcAddr! };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("agent:", output);
        Assert.Contains("runtime:", output);
        Assert.Empty(error);
    }

    [Fact(Timeout = 10000)]
    public async Task InfoCommand_JsonFormat_OutputsJson()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(InfoCommand.Create());

        var args = new[] { "info", "--rpc-addr", fixture.RpcAddr!, "--format", "json" };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("{", output);
    }
}
