// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Integration")]
[Collection("Sequential")]
public class ForceLeaveCommandTests
{
    [Fact(Timeout = 10000)]
    public async Task ForceLeaveCommand_WithValidNode_Succeeds()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(ForceLeaveCommand.Create());

        var args = new[] { "force-leave", "--rpc-addr", fixture.RpcAddr!, "dummy-node" };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Force leave request sent for node: dummy-node", output);
    }
}
