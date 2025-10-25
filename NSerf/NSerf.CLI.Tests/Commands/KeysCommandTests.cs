// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;

namespace NSerf.CLI.Tests.Commands;

/// <summary>
/// Tests for the Keys command - encryption key management.
/// Port of: serf/cmd/serf/command/keys_test.go
/// Essential for production security and key rotation.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public class KeysCommandTests
{
    // Test encryption keys from Go tests
    private const string TestKey1 = "ZWTL+bgjHyQPhJRKcFe3ccirc2SFHmc/Nw67l8NQfdk=";
    private const string TestKey2 = "WbL6oaTPom+7RG7Q/INbJWKy09OLar/Hf2SuOAdoQE4=";
    private const string TestKey3 = "HvY8ubRZMgafUOWvrOadwOckVa1wN3QWAo46FVKbVN8=";

    /// <summary>
    /// Test listing keys when encryption is not enabled.
    /// Port of: TestKeysCommandRun_ListKeysFailure
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task KeysCommand_List_NoEncryption_Fails()
    {
        // Arrange - agent without encryption
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();
        
        var rootCommand = new RootCommand();
        rootCommand.Add(KeysCommand.Create());
        
        var args = new[]
        {
            "keys",
            "list",
            "--rpc-addr", fixture.RpcAddr!
        };
        
        var errorWriter = new StringWriter();
        var originalError = Console.Error;
        
        try
        {
            Console.SetError(errorWriter);
            
            // Act
            var exitCode = await rootCommand.Parse(args).InvokeAsync();
            
            // Assert - should fail with exit code 1
            Assert.Equal(1, exitCode);
            
            var errorText = errorWriter.ToString();
            Assert.Contains("Error:", errorText);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
    
    /// <summary>
    /// Test invalid RPC address for keys command.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task KeysCommand_InvalidRpcAddress_Fails()
    {
        // Arrange
        var rootCommand = new RootCommand();
        rootCommand.Add(KeysCommand.Create());
        
        var args = new[]
        {
            "keys",
            "list",
            "--rpc-addr", "127.0.0.1:19999"
        };
        
        var errorWriter = new StringWriter();
        var originalError = Console.Error;
        
        try
        {
            Console.SetError(errorWriter);
            
            // Act
            var exitCode = await rootCommand.Parse(args).InvokeAsync();
            
            // Assert
            Assert.Equal(1, exitCode);
            Assert.Contains("Error:", errorWriter.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
    
    // Note: Full encryption key management tests (install, use, remove) require
    // an agent with encryption enabled and keyring support. These would need:
    // 1. AgentFixture enhancement to support encryption configuration
    // 2. Keyring file management
    // 3. Multiple keys in keyring for testing use/remove operations
    //
    // For now, we've verified the command structure and error handling.
    // Full integration tests can be added when keyring support is complete in SerfAgent.
}
