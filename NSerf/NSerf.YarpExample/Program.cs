using NSerf.Extensions;
using NSerf.YarpExample;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Get configuration from command line or use defaults
var nodeName = args.Length > 0 ? args[0] : $"proxy-{Environment.MachineName}";
var proxyPort = args.Length > 1 ? int.Parse(args[1]) : 8080;
var serfPort = args.Length > 2 ? int.Parse(args[2]) : 7946;
var joinNode = args.Length > 3 ? args[3] : null;

// Get encryption key from environment
var encryptKey = Environment.GetEnvironmentVariable("SERF_ENCRYPT_KEY");

// Configure web host
builder.WebHost.UseUrls($"http://0.0.0.0:{proxyPort}");

// Add Serf for service discovery
builder.Services.AddSerf(options =>
{
    options.NodeName = nodeName;
    options.BindAddr = $"0.0.0.0:{serfPort}";
    options.Tags["role"] = "proxy";
    options.Tags["http-port"] = proxyPort.ToString();
    options.RejoinAfterLeave = true;
    
    // Enable snapshot persistence
    options.SnapshotPath = "/serf/snapshots/serf.snapshot";
    Console.WriteLine($"[Snapshot] Enabled at /serf/snapshots/serf.snapshot");

    // Enable encryption if key provided (must match backend services)
    if (!string.IsNullOrEmpty(encryptKey))
    {
        options.EncryptKey = encryptKey;
        Console.WriteLine($"[Security] Encryption enabled");
    }

    if (!string.IsNullOrEmpty(joinNode))
    {
        options.StartJoin = new[] { joinNode };
        options.RetryJoin = new[] { joinNode };
    }
});

// Add YARP with NSerf service discovery
builder.Services.AddSingleton<SerfServiceDiscoveryProvider>();
builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<SerfServiceDiscoveryProvider>());
builder.Services.AddReverseProxy();

var app = builder.Build();

// Admin endpoints
app.MapGet("/proxy/health", () => Results.Ok(new
{
    status = "healthy",
    proxy = nodeName,
    timestamp = DateTime.UtcNow
}));

app.MapGet("/proxy/members", (NSerf.Agent.SerfAgent agent) =>
{
    if (agent.Serf == null)
        return Results.Problem("Serf not started");

    var members = agent.Serf.Members()
        .Select(m => new
        {
            m.Name,
            Status = m.Status.ToString(),
            m.Tags,
            Address = $"{m.Addr}:{m.Port}"
        })
        .ToList();

    return Results.Ok(new
    {
        proxy = nodeName,
        memberCount = members.Count,
        members,
        backends = members.Count(m => m.Tags.ContainsKey("service") && m.Tags["service"] == "backend" && m.Status == "Alive")
    });
});


app.MapReverseProxy();

await app.RunAsync();
