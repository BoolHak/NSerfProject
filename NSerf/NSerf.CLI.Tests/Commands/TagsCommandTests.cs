// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Integration")]
[Collection("Sequential")]
public class TagsCommandTests
{
    [Fact(Timeout = 10000)]
    public async Task TagsCommand_SetTags_Succeeds()
    {
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(TagsCommand.Create());

        var args = new[] { "tags", "--rpc-addr", fixture.RpcAddr!, "--set", "foo=bar" };

        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        Assert.Equal(0, exitCode);
        Assert.Contains("Successfully updated agent tags", output);
    }

    [Fact(Timeout = 10000)]
    public async Task TagsCommand_DeleteTags_Succeeds()
    {
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(TagsCommand.Create());

        var args = new[] { "tags", "--rpc-addr", fixture.RpcAddr!, "--delete", "role" };

        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        Assert.Equal(0, exitCode);
        Assert.Contains("Successfully updated agent tags", output);
    }
}
