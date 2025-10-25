// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using System.Security.Cryptography;

namespace NSerf.CLI.Commands;

/// <summary>
/// Keygen command - generates a new encryption key.
/// </summary>
public static class KeygenCommand
{
    public static Command Create()
    {
        var command = new Command("keygen", "Generate a new encryption key");

        command.SetAction((parseResult, cancellationToken) =>
        {
            var key = GenerateKey();
            Console.WriteLine(key);
            return Task.CompletedTask;
        });

        return command;
    }

    private static string GenerateKey()
    {
        var keyBytes = new byte[32]; // 256 bits
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        return Convert.ToBase64String(keyBytes);
    }
}
