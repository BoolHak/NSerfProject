using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using NSerf.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Get configuration from the command line or use defaults
var nodeName = args.Length > 0 ? args[0] : $"backend-{Environment.MachineName}-{Random.Shared.Next(1000, 9999)}";
var httpPort = args.Length > 1 ? int.Parse(args[1]) : 5000;
var serfPort = args.Length > 2 ? int.Parse(args[2]) : 7946;
var joinNode = args.Length > 3 ? args[3] : null;

// Get encryption key from the environment
var encryptKey = Environment.GetEnvironmentVariable("SERF_ENCRYPT_KEY");

// Configure web host
builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");

// Add Serf for service registration
builder.Services.AddNSerf(options =>
{
    options.NodeName = nodeName;
    options.BindAddr = $"0.0.0.0:{serfPort}";
    options.Tags["service"] = "backend";
    options.Tags["http-port"] = httpPort.ToString();
    options.Tags["version"] = "1.0";
    options.RejoinAfterLeave = true;
    
    // Enable snapshot persistence
    // Enable snapshot persistence
    var snapshotDir = Path.Combine(AppContext.BaseDirectory, "snapshots");
    Directory.CreateDirectory(snapshotDir);
    options.SnapshotPath = Path.Combine(snapshotDir, "serf.snapshot");
    Console.WriteLine($"[Snapshot] Enabled at {options.SnapshotPath}");

    // Enable encryption if key provided
    if (!string.IsNullOrEmpty(encryptKey))
    {
        options.EncryptKey = encryptKey;
        Console.WriteLine($"[Security] Encryption enabled");
    }

    if (string.IsNullOrEmpty(joinNode)) return;
    
    options.StartJoin = [joinNode];
    options.RetryJoin = [joinNode];
    options.RetryJoin = [joinNode];
});

// Configure Data Protection to persist keys (critical for Docker environments)
var keysDir = Path.Combine(AppContext.BaseDirectory, "keys");
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("NSerf.BackendService");

// Configure Dashboard if enabled
var enableDashboard = builder.Configuration.GetValue<bool>("NSerf:Dashboard:Enabled", true);
if (enableDashboard)
{
    builder.Services.AddControllersWithViews();
    // Register monitoring service
    builder.Services.AddSingleton<NSerf.BackendService.Services.NetworkTrafficMonitor>();
    
    // Add services to the container.
    builder.Services.AddLogging(logging =>
    {
        logging.AddFilter("NSerf", LogLevel.Debug);
    logging.SetMinimumLevel(LogLevel.Debug);
    logging.AddConsole();
});
    // Add custom logger provider to capture Serf/Memberlist logs
    builder.Logging.Services.AddSingleton<ILoggerProvider, NSerf.BackendService.Logging.TrafficLoggerProvider>();

    builder.Services.AddSignalR();
    builder.Services.AddHostedService<NSerf.BackendService.Services.DashboardEventHandler>();
    Console.WriteLine($"[Dashboard] Enabled at /serf");
}

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

// Map Dashboard if enabled
if (enableDashboard)
{
    app.UseStaticFiles();
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Dashboard}/{action=Index}/{id?}");
    app.MapHub<NSerf.BackendService.Hubs.SerfHub>("/serfHub");
}

await app.RunAsync();

