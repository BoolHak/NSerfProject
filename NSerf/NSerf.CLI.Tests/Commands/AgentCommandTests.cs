// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;
using NSerf.CLI.Tests.Helpers;
using NSerf.Client;

namespace NSerf.CLI.Tests.Commands;

/// <summary>
/// Tests for the Agent command - the core long-running command that starts a Serf agent.
/// Ported from: serf/cmd/serf/command/agent/command_test.go
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public class AgentCommandTests
{
    /// <summary>
    /// Test that agent runs until shutdown signal is sent.
    /// Port of: TestCommandRun
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task AgentCommand_RunsUntilShutdown()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var bindAddr = TestHelper.GetRandomBindAddr();
        var rpcAddr = "127.0.0.1:0";

        var rootCommand = new RootCommand();
        rootCommand.Add(AgentCommand.Create(cts.Token));

        var args = new[]
        {
            "agent",
            "--bind", bindAddr,
            "--rpc-addr", rpcAddr
        };

        // Act - start agent in background
        var agentTask = Task.Run(async () => await rootCommand.Parse(args).InvokeAsync());

        // Wait for agent to start
        await Task.Delay(1000);

        // Verify it's running (doesn't exit immediately)
        Assert.False(agentTask.IsCompleted, "Agent should still be running");

        // Act - send shutdown signal
        cts.Cancel();

        // Assert - agent shuts down gracefully
        var exitCodeTask = await Task.WhenAny(agentTask, Task.Delay(5000));
        Assert.True(exitCodeTask == agentTask, "Agent should shutdown within timeout");

        var exitCode = await agentTask;
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// Test that RPC server starts and accepts connections.
    /// Port of: TestCommandRun_rpc
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task AgentCommand_RpcServerAcceptsConnections()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var bindAddr = TestHelper.GetRandomBindAddr();
        var rpcAddr = "127.0.0.1:1332";

        var rootCommand = new RootCommand();
        rootCommand.Add(AgentCommand.Create(cts.Token));

        var args = new[]
        {
            "agent",
            "--bind", bindAddr,
            "--rpc-addr", rpcAddr
        };

        // Act - start agent in background
        var agentTask = Task.Run(async () => await rootCommand.Parse(args).InvokeAsync());

        // Wait for agent and RPC server to start
        await Task.Delay(500);

        try
        {
            // Assert - can connect via RPC and query members
            using var client = new RpcClient(new RpcConfig { Address = rpcAddr });
            await client.ConnectAsync();

            var members = await client.MembersAsync();
            Assert.Single(members); // Should see ourselves
        }
        finally
        {
            // Cleanup
            cts.Cancel();
            await Task.WhenAny(agentTask, Task.Delay(2000));
        }
    }

    /// <summary>
    /// Test that agent can join another agent at startup.
    /// Port of: TestCommandRun_join
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task AgentCommand_JoinsClusterAtStartup()
    {
        // Arrange - create first agent
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();

        // Get actual bind address from the local member (not the config with :0)
        var localMember = fixture.Agent!.Serf!.Members()[0];
        var agent1BindAddr = $"{localMember.Addr}:{localMember.Port}";

        // Arrange - prepare second agent to join first
        using var cts = new CancellationTokenSource();
        var bindAddr = TestHelper.GetRandomBindAddr();

        var rootCommand = new RootCommand();
        rootCommand.Add(AgentCommand.Create(cts.Token));

        var args = new[]
        {
            "agent",
            "--bind", bindAddr,
            "--join", agent1BindAddr,
            "--replay"
        };

        // Act - start second agent that joins first
        var agentTask = Task.Run(async () => await rootCommand.Parse(args).InvokeAsync());

        // Wait for join to complete
        await Task.Delay(1000);

        try
        {
            // Assert - first agent should see both members
            var members = fixture.Agent!.Serf!.Members();
            Assert.Equal(2, members.Length);
        }
        finally
        {
            // Cleanup
            cts.Cancel();
            await Task.WhenAny(agentTask, Task.Delay(2000));
        }
    }

    /// <summary>
    /// Test that agent fails if join target doesn't exist.
    /// Port of: TestCommandRun_joinFail
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task AgentCommand_JoinFailure_ExitsWithError()
    {
        // Arrange
        var bindAddr = TestHelper.GetRandomBindAddr();
        var nonExistentAddr = "127.0.0.1:19999"; // Nothing listening here

        var rootCommand = new RootCommand();
        rootCommand.Add(AgentCommand.Create(CancellationToken.None));

        var args = new[]
        {
            "agent",
            "--bind", bindAddr,
            "--join", nonExistentAddr
        };

        // Act
        var exitCode = await rootCommand.Parse(args).InvokeAsync();

        // Assert - should fail (non-zero exit code)
        Assert.NotEqual(0, exitCode);
    }

}
