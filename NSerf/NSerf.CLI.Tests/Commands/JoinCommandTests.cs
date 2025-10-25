// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Integration")]
[Collection("Sequential")]
public class JoinCommandTests
{
    [Fact(Timeout = 10000)]
    public async Task JoinCommand_WithValidAddress_Succeeds()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        await using var fixture2 = new AgentFixture();
        await fixture2.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(JoinCommand.Create());

        var member2 = fixture2.Agent!.Serf!.LocalMember();
        var addr2 = $"{member2.Name}/{member2.Addr}:{member2.Port}";
        var args = new[] { "join", "--rpc-addr", fixture.RpcAddr!, addr2 };

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
        Assert.Contains("Successfully joined cluster", output);
        Assert.Empty(error);
    }

    [Fact(Timeout = 5000)]
    public async Task JoinCommand_NoAddresses_Fails()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(JoinCommand.Create());

        var args = new[] { "join", "--rpc-addr", fixture.RpcAddr! };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("At least one address", error);
    }

    [Fact(Timeout = 10000)]
    public async Task JoinCommand_WithReplay_PassesFlag()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        await using var fixture2 = new AgentFixture();
        await fixture2.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(JoinCommand.Create());

        var member2 = fixture2.Agent!.Serf!.LocalMember();
        var addr2 = $"{member2.Name}/{member2.Addr}:{member2.Port}";
        var args = new[] { "join", "--rpc-addr", fixture.RpcAddr!, "--replay", addr2 };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Successfully joined cluster", output);
    }
}
