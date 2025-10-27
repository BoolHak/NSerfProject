using System.Diagnostics;
using NSerf.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Get configuration from command line or use defaults
var nodeName = args.Length > 0 ? args[0] : $"backend-{Environment.MachineName}-{Random.Shared.Next(1000, 9999)}";
var httpPort = args.Length > 1 ? int.Parse(args[1]) : 5000;
var serfPort = args.Length > 2 ? int.Parse(args[2]) : 7946;
var joinNode = args.Length > 3 ? args[3] : null;

// Get encryption key from environment
var encryptKey = Environment.GetEnvironmentVariable("SERF_ENCRYPT_KEY");

// Configure web host
builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");

// Add Serf for service registration
builder.Services.AddSerf(options =>
{
    options.NodeName = nodeName;
    options.BindAddr = $"0.0.0.0:{serfPort}";
    options.Tags["service"] = "backend";
    options.Tags["http-port"] = httpPort.ToString();
    options.Tags["version"] = "1.0";
    options.RejoinAfterLeave = true;

    // Enable encryption if key provided
    if (!string.IsNullOrEmpty(encryptKey))
    {
        options.EncryptKey = encryptKey;
        Console.WriteLine($"[Security] Encryption enabled");
    }

    options.RejoinAfterLeave = true;

    if (!string.IsNullOrEmpty(joinNode))
    {
        options.StartJoin = new[] { joinNode };
        options.RetryJoin = new[] { joinNode };
    }
});

var app = builder.Build();


// Health check endpoint (required for YARP health checks)
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = nodeName,
    timestamp = DateTime.UtcNow
}));

// Sample API endpoint - returns instance information
app.MapGet("/api/info", () => Results.Ok(new
{
    instance = nodeName,
    httpPort,
    serfPort,
    timestamp = DateTime.UtcNow,
    uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()
}));

// Sample API endpoint - simulates work
app.MapGet("/api/work/{id}", async (string id) =>
{
    // Simulate processing time
    await Task.Delay(Random.Shared.Next(100, 500));

    return Results.Ok(new
    {
        id,
        processedBy = nodeName,
        timestamp = DateTime.UtcNow,
        result = $"Work {id} completed successfully"
    });
});

// Sample API endpoint - get cluster members
app.MapGet("/api/cluster", (NSerf.Agent.SerfAgent agent) =>
{
    if (agent.Serf == null)
        return Results.Problem("Serf not started");

    var members = agent.Serf.Members()
        .Where(m => m.Status == NSerf.Serf.MemberStatus.Alive)
        .Select(m => new
        {
            m.Name,
            m.Tags,
            Address = $"{m.Addr}:{m.Port}"
        })
        .ToList();

    return Results.Ok(new
    {
        self = nodeName,
        totalMembers = members.Count,
        backends = members.Count(m => m.Tags.ContainsKey("service") && m.Tags["service"] == "backend"),
        proxies = members.Count(m => m.Tags.ContainsKey("role") && m.Tags["role"] == "proxy"),
        members
    });
});

await app.RunAsync();

