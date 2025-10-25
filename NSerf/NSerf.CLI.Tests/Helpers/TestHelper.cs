// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Net.Sockets;
using NSerf.Agent;
using NSerf.Agent.RPC;

namespace NSerf.CLI.Tests.Helpers;

/// <summary>
/// Test helper utilities for CLI tests.
/// </summary>
public static class TestHelper
{
    /// <summary>
    /// Creates a test agent with default configuration.
    /// </summary>
    public static async Task<SerfAgent> CreateTestAgentAsync(
        string? bindAddr = null,
        CancellationToken cancellationToken = default)
    {
        var actualBindAddr = bindAddr ?? GetRandomBindAddr();
        
        var config = new AgentConfig
        {
            NodeName = actualBindAddr,
            BindAddr = actualBindAddr + ":0", // Random port
            Tags = new Dictionary<string, string>
            {
                ["role"] = "test",
                ["tag1"] = "foo",
                ["tag2"] = "bar"
            }
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync(cancellationToken);
        return agent;
    }

    /// <summary>
    /// Creates a test RPC server for the given agent.
    /// </summary>
    public static async Task<(string rpcAddr, RpcServer server)> CreateTestRpcAsync(
        SerfAgent agent,
        string? authKey = null,
        CancellationToken cancellationToken = default)
    {
        var rpcAddr = GetRandomRpcAddr();
        var server = new RpcServer(agent, rpcAddr, authKey);
        await server.StartAsync(cancellationToken);
        
        // Get actual address (in case port was 0)
        var actualAddr = server.Address ?? rpcAddr;
        
        return (actualAddr, server);
    }

    /// <summary>
    /// Gets a random available bind address.
    /// </summary>
    public static string GetRandomBindAddr()
    {
        return $"127.0.0.{Random.Shared.Next(2, 255)}";
    }

    /// <summary>
    /// Gets a random available RPC address.
    /// </summary>
    public static string GetRandomRpcAddr()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return $"127.0.0.1:{port}";
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Waits for a condition with timeout.
    /// </summary>
    public static async Task<bool> WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            
            await Task.Delay(50);
        }
        
        return false;
    }
}
