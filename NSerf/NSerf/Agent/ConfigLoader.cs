// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace NSerf.Agent;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters =
        {
            new GoDurationJsonConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
        }
    };

    public static async Task<AgentConfig> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Configuration file not found: {path}");

        var json = await File.ReadAllTextAsync(path, cancellationToken);

        try
        {
            var config = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize configuration");

            return config;
        }
        catch (JsonException ex) when (ex.Message.Contains("not be mapped", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigException($"Unknown configuration directive in {path}: {ex.Message}", ex);
        }
    }

    public static async Task<AgentConfig> LoadFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Configuration directory not found: {directoryPath}");

        var result = AgentConfig.Default();

        var jsonFiles = Directory.GetFiles(directoryPath, "*.json")
            .OrderBy(f => f, StringComparer.Ordinal);  // Lexical order

        foreach (var file in jsonFiles)
        {
            var config = await LoadFromFileAsync(file, cancellationToken);
            result = AgentConfig.Merge(result, config);
        }

        return result;
    }

    public static async Task<Dictionary<string, string>> LoadTagsFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return [];

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        return tags ?? [];
    }

    public static async Task SaveTagsToFileAsync(string path, Dictionary<string, string> tags, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(tags, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public static async Task<string[]> LoadKeyringFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return [];

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var keys = JsonSerializer.Deserialize<string[]>(json);

        return keys ?? [];
    }

    public static AgentConfig LoadFromArgs(string[] args)
    {
        var config = new AgentConfig();
        var i = 0;

        while (i < args.Length)
        {
            var arg = args[i];
            i++;

            switch (arg)
            {
                case "-node" or "--node":
                    config.NodeName = ConsumeNextArg(args, ref i, arg);
                    break;

                case "-bind" or "--bind":
                    config.BindAddr = ConsumeNextArg(args, ref i, arg);
                    break;

                case "-rpc-addr" or "--rpc-addr":
                    config.RpcAddr = ConsumeNextArg(args, ref i, arg);
                    break;

                case "-rpc-auth" or "--rpc-auth":
                    config.RpcAuthKey = ConsumeNextArg(args, ref i, arg);
                    break;

                case "-join" or "--join":
                    config.StartJoin = [.. ConsumeMultipleArgs(args, ref i)];
                    break;

                case "-tag" or "--tag":
                    ParseAndAddTag(config, ConsumeNextArg(args, ref i, arg));
                    break;

                case "-log-level" or "--log-level":
                    config.LogLevel = ConsumeNextArg(args, ref i, arg);
                    break;

                case "-snapshot" or "--snapshot":
                    config.SnapshotPath = ConsumeNextArg(args, ref i, arg);
                    break;

                case "-rejoin" or "--rejoin":
                    config.RejoinAfterLeave = true;
                    break;

                case "-config-file" or "--config-file":
                    // Config file will be loaded separately
                    break;
            }
        }

        return config;
    }

    private static string ConsumeNextArg(string[] args, ref int index, string argName)
    {
        return index >= args.Length ? throw new ArgumentException($"Missing value for argument '{argName}'") : args[index++];
    }

    private static List<string> ConsumeMultipleArgs(string[] args, ref int index)
    {
        var results = new List<string>();
        while (index < args.Length && !args[index].StartsWith('-'))
        {
            results.Add(args[index++]);
        }
        return results;
    }

    private static void ParseAndAddTag(AgentConfig config, string tagValue)
    {
        var tagParts = tagValue.Split('=', 2);
        if (tagParts.Length == 2)
        {
            config.Tags[tagParts[0]] = tagParts[1];
        }
    }

    public static void Validate(AgentConfig config)
    {
        if (string.IsNullOrEmpty(config.NodeName))
            config.NodeName = Environment.MachineName;

        if (string.IsNullOrEmpty(config.BindAddr))
            config.BindAddr = "0.0.0.0:7946";

        if (string.IsNullOrEmpty(config.RpcAddr))
            config.RpcAddr = "127.0.0.1:7373";

        if (string.IsNullOrEmpty(config.LogLevel))
            config.LogLevel = "INFO";
    }
}
