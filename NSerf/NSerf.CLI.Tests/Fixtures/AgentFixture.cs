// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.Agent.RPC;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests.Fixtures;

/// <summary>
/// Test fixture for creating and managing a test agent with RPC server.
/// </summary>
public class AgentFixture : IAsyncDisposable
{
    public SerfAgent? Agent { get; private set; }
    public RpcServer? RpcServer { get; private set; }
    public string? RpcAddr { get; private set; }
    public string? AuthKey { get; private set; }

    /// <summary>
    /// Initializes the fixture with a test agent and RPC server.
    /// </summary>
    public async Task InitializeAsync(
        string? authKey = null,
        CancellationToken cancellationToken = default)
    {
        AuthKey = authKey;
        
        Agent = await TestHelper.CreateTestAgentAsync(
            cancellationToken: cancellationToken);
        
        (RpcAddr, RpcServer) = await TestHelper.CreateTestRpcAsync(
            Agent,
            authKey,
            cancellationToken);
        
        // Wait for server and agent to be fully ready
        await Task.Delay(500, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (RpcServer != null)
        {
            await RpcServer.DisposeAsync();
        }

        if (Agent != null)
        {
            await Agent.ShutdownAsync();
            await Agent.DisposeAsync();
        }
    }
}
