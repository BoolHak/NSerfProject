// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Integration")]
[Collection("Sequential")]
public class QueryCommandTests
{
    [Fact(Timeout = 10000)]
    public async Task QueryCommand_Dispatches()
    {
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        var rootCommand = new RootCommand();
        rootCommand.Add(QueryCommand.Create());

        var args = new[] { "query", "--rpc-addr", fixture.RpcAddr!, "test-query" };

        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        if (exitCode != 0)
        {
            Console.WriteLine($"EXIT CODE: {exitCode}");
            Console.WriteLine($"OUTPUT: {output}");
            Console.WriteLine($"ERROR: {error}");
        }
        Assert.Equal(0, exitCode);
        Assert.Contains("Query 'test-query' dispatched", output);
    }
}
