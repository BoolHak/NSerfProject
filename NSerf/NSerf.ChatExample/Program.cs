using NSerf.Extensions;
using NSerf.ChatExample;

var builder = WebApplication.CreateBuilder(args);

// Get instance name from command line or use default
var instanceName = args.Length > 0 ? args[0] : $"chat-{Environment.MachineName}-{Random.Shared.Next(1000, 9999)}";
var port = args.Length > 1 ? int.Parse(args[1]) : 5000;
var serfPort = args.Length > 2 ? int.Parse(args[2]) : 7946;
var joinNode = args.Length > 3 ? args[3] : null;

// Configure web host
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Add SignalR for real-time communication
builder.Services.AddSignalR();

// Add Serf for cluster membership and message broadcasting
builder.Services.AddSerf(options =>
{
    options.NodeName = instanceName;
    options.BindAddr = $"0.0.0.0:{serfPort}";
    options.Tags["role"] = "chat-server";
    options.Tags["http-port"] = port.ToString();
    options.RejoinAfterLeave = true;
    
    if (!string.IsNullOrEmpty(joinNode))
    {
        options.StartJoin = new[] { joinNode };
        options.RetryJoin = new[] { joinNode };
    }
    
    // Register event handler for Serf events
    options.EventHandlers.Add("user=echo [SERF] User event: $SERF_EVENT");
});

var app = builder.Build();

// Serve static files (index.html) - UseDefaultFiles must come BEFORE UseStaticFiles
app.UseDefaultFiles();
app.UseStaticFiles();

// Map SignalR hub
app.MapHub<ChatHub>("/chatHub");

// Simple health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", instance = instanceName }));

// Endpoint to show cluster members
app.MapGet("/members", (NSerf.Agent.SerfAgent agent) =>
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
    
    return Results.Ok(new { cluster = instanceName, memberCount = members.Count, members });
});

Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  NSerf Distributed Chat Example                              ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Instance:   {instanceName,-48} ║");
Console.WriteLine($"║  HTTP Port:  {port,-48} ║");
Console.WriteLine($"║  Serf Port:  {serfPort,-48} ║");
Console.WriteLine($"║  Join Node:  {(joinNode ?? "none (first node)"),-48} ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Chat UI:    http://localhost:{port,-37} ║");
Console.WriteLine($"║  Members:    http://localhost:{port}/members{"",-26} ║");
Console.WriteLine($"║  Health:     http://localhost:{port}/health{"",-27} ║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");

app.Run();
