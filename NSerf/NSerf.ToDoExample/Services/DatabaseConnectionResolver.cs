using NSerf.ServiceDiscovery;

namespace NSerf.ToDoExample.Services;

/// <summary>
/// Resolves PostgreSQL connection string using NSerf service discovery
/// </summary>
public class DatabaseConnectionResolver(
    IServiceRegistry registry,
    ILogger<DatabaseConnectionResolver> logger)
{
    public string GetConnectionString(string serviceName = "postgres")
    {
        var instances = registry.GetHealthyInstances(serviceName);

        if (instances.Count == 0)
        {
            logger.LogWarning("No healthy PostgreSQL instances found via NSerf, using fallback");
            throw new InvalidOperationException("Missing PostgreSQL instance");
        }

        var instance = instances[0];
        
        logger.LogInformation(
            "✅ Discovered PostgreSQL at {Host}:{Port} via NSerf gossip",
            instance.Host, instance.Port);

        var username = instance.Metadata.GetValueOrDefault("username", "postgres");
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";
        var database = instance.Metadata.GetValueOrDefault("database", "tododb");

        return $"Host={instance.Host};Port={instance.Port};Database={database};Username={username};Password={password}";
    }
}
