// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;

namespace NSerf.CLI.Tests.Commands;

/// <summary>
/// Tests for the Reachability command - network connectivity diagnostics.
/// Helps diagnose network and configuration issues.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public class ReachabilityCommandTests
{
    /// <summary>
    /// Test reachability command with an existing node.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task ReachabilityCommand_WithExistingNode_ShowsStatus()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();
        
        var localMember = fixture.Agent!.Serf!.Members()[0];
        var nodeName = localMember.Name;
        
        var rootCommand = new RootCommand();
        rootCommand.Add(ReachabilityCommand.Create());
        
        var args = new[]
        {
            "reachability",
            nodeName,
            "--rpc-addr", fixture.RpcAddr!
        };
        
        var output = new StringWriter();
        var originalOut = Console.Out;
        
        try
        {
            Console.SetOut(output);
            
            // Act
            var exitCode = await rootCommand.Parse(args).InvokeAsync();
            
            // Assert
            Assert.Equal(0, exitCode);
            var outputText = output.ToString();
            Assert.Contains(nodeName, outputText);
            Assert.Contains("alive", outputText);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
    
    /// <summary>
    /// Test reachability command with a non-existent node.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task ReachabilityCommand_WithNonExistentNode_ShowsNotFound()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();
        
        var rootCommand = new RootCommand();
        rootCommand.Add(ReachabilityCommand.Create());
        
        var args = new[]
        {
            "reachability",
            "non-existent-node",
            "--rpc-addr", fixture.RpcAddr!
        };
        
        var output = new StringWriter();
        var originalOut = Console.Out;
        
        try
        {
            Console.SetOut(output);
            
            // Act
            var exitCode = await rootCommand.Parse(args).InvokeAsync();
            
            // Assert
            Assert.Equal(0, exitCode);
            var outputText = output.ToString();
            Assert.Contains("not found", outputText);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
    
    /// <summary>
    /// Test reachability command with invalid RPC address.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task ReachabilityCommand_InvalidRpcAddress_Fails()
    {
        // Arrange
        var rootCommand = new RootCommand();
        rootCommand.Add(ReachabilityCommand.Create());
        
        var args = new[]
        {
            "reachability",
            "test-node",
            "--rpc-addr", "127.0.0.1:19999"
        };
        
        var errorWriter = new StringWriter();
        var originalError = Console.Error;
        
        try
        {
            Console.SetError(errorWriter);
            
            // Act - command catches exception and writes to stderr
            var exitCode = await rootCommand.Parse(args).InvokeAsync();
            
            // Assert
            var errorText = errorWriter.ToString();
            Assert.Contains("Error:", errorText);
            Assert.Contains("Failed to connect", errorText);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
