using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests;

[Collection("Sequential")]
public class VersionCommandTests
{
    [Fact(Timeout = 5000)]
    public async Task VersionCommand_ShowsVersionInfo()
    {
        // Arrange
        var rootCommand = new RootCommand();
        rootCommand.Add(VersionCommand.Create());

        var args = new[] { "version" };

        // Act
        var (exitCode, output, error) = await CommandTestHelper.ExecuteCommandAsync(rootCommand, args);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("NSerf v", output);
        Assert.Contains("C# port of HashiCorp Serf", output);
        Assert.Empty(error);
    }
}