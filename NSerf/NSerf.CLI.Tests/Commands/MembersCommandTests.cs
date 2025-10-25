// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Integration")]
[Collection("Sequential")]
public class MembersCommandTests
{
    [Fact(Timeout = 10000)]
    public async Task MembersCommand_WithRunningAgent_ListsMembers()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(MembersCommand.Create());

        var args = new[] { "members", "--rpc-addr", fixture.RpcAddr! };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains(fixture.Agent!.NodeName, output);
        Assert.Contains("alive", output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(error);
    }

    [Fact(Timeout = 10000)]
    public async Task MembersCommand_WithStatusFilter_FiltersCorrectly()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(MembersCommand.Create());

        // First check without filter to see what members exist
        var argsNoFilter = new[] { "members", "--rpc-addr", fixture.RpcAddr! };
        var (exitCode0, output0, error0) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, argsNoFilter);
        Console.WriteLine($"MEMBERS WITHOUT FILTER: {output0}");
        
        var args = new[] { "members", "--rpc-addr", fixture.RpcAddr!, "--status", "alive" };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        if (!output.Contains(fixture.Agent!.NodeName))
        {
            Console.WriteLine($"EXIT CODE: {exitCode}");
            Console.WriteLine($"OUTPUT: {output}");
            Console.WriteLine($"ERROR: {error}");
            Console.WriteLine($"EXPECTED NODE: {fixture.Agent.NodeName}");
        }
        Assert.Equal(0, exitCode);
        Assert.Contains(fixture.Agent!.NodeName, output);
        Assert.DoesNotContain("failed", output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(error);
    }

    [Fact(Timeout = 10000)]
    public async Task MembersCommand_JsonFormat_OutputsJson()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(MembersCommand.Create());

        var args = new[] { "members", "--rpc-addr", fixture.RpcAddr!, "--format", "json" };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        if (exitCode != 0)
        {
            Console.WriteLine($"EXIT CODE: {exitCode}");
            Console.WriteLine($"OUTPUT: {output}");
            Console.WriteLine($"ERROR: {error}");
        }
        Assert.Equal(0, exitCode);
        Assert.Contains("{", output);
        Assert.Contains("members", output);
        Assert.Contains(fixture.Agent!.NodeName, output);
        Assert.Empty(error);
    }

    [Fact(Timeout = 10000)]
    public async Task MembersCommand_InvalidRpcAddress_ReturnsError()
    {
        // Arrange
        var rootCommand = new RootCommand();
        rootCommand.Add(MembersCommand.Create());

        var args = new[] { "members", "--rpc-addr", "127.0.0.1:9999" }; // Non-existent

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("Error", error);
    }
}
