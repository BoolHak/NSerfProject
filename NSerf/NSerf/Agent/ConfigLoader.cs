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
            var config = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions);
            
            if (config == null)
                throw new InvalidOperationException("Failed to deserialize configuration");

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
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        
        return tags ?? new Dictionary<string, string>();
    }

    public static async Task SaveTagsToFileAsync(string path, Dictionary<string, string> tags, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(tags, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public static async Task<string[]> LoadKeyringFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var keys = JsonSerializer.Deserialize<string[]>(json);
        
        return keys ?? Array.Empty<string>();
    }

    public static AgentConfig LoadFromArgs(string[] args)
    {
        var config = new AgentConfig();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-node":
                case "--node":
                    config.NodeName = args[++i];
                    break;

                case "-bind":
                case "--bind":
                    config.BindAddr = args[++i];
                    break;

                case "-rpc-addr":
                case "--rpc-addr":
                    config.RPCAddr = args[++i];
                    break;

                case "-rpc-auth":
                case "--rpc-auth":
                    config.RPCAuthKey = args[++i];
                    break;

                case "-join":
                case "--join":
                    var joins = new List<string>();
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        joins.Add(args[++i]);
                    }
                    config.StartJoin = joins.ToArray();
                    break;

                case "-tag":
                case "--tag":
                    var tagParts = args[++i].Split('=', 2);
                    if (tagParts.Length == 2)
                    {
                        config.Tags[tagParts[0]] = tagParts[1];
                    }
                    break;

                case "-log-level":
                case "--log-level":
                    config.LogLevel = args[++i];
                    break;

                case "-snapshot":
                case "--snapshot":
                    config.SnapshotPath = args[++i];
                    break;

                case "-rejoin":
                case "--rejoin":
                    config.RejoinAfterLeave = true;
                    break;

                case "-config-file":
                case "--config-file":
                    // Config file will be loaded separately
                    break;
            }
        }

        return config;
    }

    public static void Validate(AgentConfig config)
    {
        if (string.IsNullOrEmpty(config.NodeName))
            config.NodeName = Environment.MachineName;

        if (string.IsNullOrEmpty(config.BindAddr))
            config.BindAddr = "0.0.0.0:7946";

        if (string.IsNullOrEmpty(config.RPCAddr))
            config.RPCAddr = "127.0.0.1:7373";

        if (string.IsNullOrEmpty(config.LogLevel))
            config.LogLevel = "INFO";
    }
}
