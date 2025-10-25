// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;

namespace NSerf.CLI.Tests.Helpers;

/// <summary>
/// Helper for testing CLI commands.
/// </summary>
public static class CommandTestHelper
{
    /// <summary>
    /// Executes a command and captures its output.
    /// </summary>
    public static async Task<(int exitCode, string output, string error)> ExecuteCommandAsync(
        RootCommand rootCommand,
        string[] args)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();

        var originalOut = Console.Out;
        var originalErr = Console.Error;

        try
        {
            Console.SetOut(outputWriter);
            Console.SetError(errorWriter);

            var task = rootCommand.Parse(args).InvokeAsync();
            var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
            
            if (completedTask != task)
            {
                return (-1, outputWriter.ToString(), "Command timed out after 10 seconds");
            }

            var exitCode = await task;
            return (exitCode, outputWriter.ToString(), errorWriter.ToString());
        }
        catch (OperationCanceledException)
        {
            return (-1, outputWriter.ToString(), "Command timed out after 10 seconds");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    /// <summary>
    /// Executes a command with timeout.
    /// </summary>
    public static async Task<(int exitCode, string output, string error)> ExecuteCommandWithTimeoutAsync(
        RootCommand rootCommand,
        string[] args,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        
        try
        {
            var task = ExecuteCommandAsync(rootCommand, args);
            var timeoutTask = Task.Delay(timeout, cts.Token);
            
            var completed = await Task.WhenAny(task, timeoutTask);
            
            if (completed == timeoutTask)
            {
                return (-1, "", "Command timed out");
            }
            
            return await task;
        }
        catch (OperationCanceledException)
        {
            return (-1, "", "Command timed out");
        }
    }
}
