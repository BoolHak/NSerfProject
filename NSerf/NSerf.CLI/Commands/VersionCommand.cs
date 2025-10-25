// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using System.Reflection;

namespace NSerf.CLI.Commands;

/// <summary>
/// Version command - displays version information.
/// </summary>
public static class VersionCommand
{
    public static Command Create()
    {
        var command = new Command("version", "Show version information");

        command.SetAction((parseResult, cancellationToken) =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? version?.ToString() ?? "unknown";

            Console.WriteLine($"NSerf v{informationalVersion}");
            Console.WriteLine("C# port of HashiCorp Serf");
            return Task.CompletedTask;
        });

        return command;
    }
}
