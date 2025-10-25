// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Unit")]
[Collection("Sequential")]
public class KeygenCommandTests
{
    [Fact(Timeout = 5000)]
    public async Task KeygenCommand_GeneratesKey()
    {
        // Arrange
        var rootCommand = new RootCommand();
        rootCommand.Add(KeygenCommand.Create());

        var args = new[] { "keygen" };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.NotEmpty(output);
        Assert.Empty(error);
        
        // Verify it's base64
        var key = output.Trim();
        Assert.True(key.Length > 40); // 32 bytes base64 encoded
        var bytes = Convert.FromBase64String(key);
        Assert.Equal(32, bytes.Length);
    }
}
