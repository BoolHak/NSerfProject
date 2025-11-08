using Microsoft.EntityFrameworkCore;
using NSerf.Extensions;
using NSerf.ServiceDiscovery;
using NSerf.ToDoExample.Data;
using NSerf.ToDoExample.Services;
using Microsoft.Extensions.Hosting;
using NSerf.ToDoExample;
using NSerf.ToDoExample.Background;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add NSerf with service discovery
builder.Services.AddNSerf(options =>
{
    options.NodeName = $"todo-api-{Environment.MachineName}-{Guid.NewGuid():N}"[..50];
    options.BindAddr = "0.0.0.0:7946";
    options.Tags["service:todo-api"] = "true";
    options.Tags["port:todo-api"] = "8080";
    options.Tags["scheme:todo-api"] = "http";
    options.SnapshotPath = "/tmp/nserf-todo-api.snapshot";
    options.RejoinAfterLeave = true;
    options.Profile = "lan"; // LAN profile for local discovery
    
    // Join seed node if specified
    var seedNode = Environment.GetEnvironmentVariable("SERF_SEED");
    if (!string.IsNullOrEmpty(seedNode))
    {
        options.StartJoin = [seedNode];
    }
});

// Add service discovery components
builder.Services.AddSingleton<IServiceRegistry, ServiceRegistry>();
builder.Services.AddSingleton<DatabaseConnectionResolver>();
builder.Services.AddHostedService<ServiceDiscoveryHostedService>();

// PostgreSQL with dynamic discovery
builder.Services.AddDbContext<TodoDbContext>((serviceProvider, options) =>
{
    var resolver = serviceProvider.GetRequiredService<DatabaseConnectionResolver>();
    var connectionString = resolver.GetConnectionString();
    options.UseNpgsql(connectionString);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

// Run migrations on startup (after service discovery has time to work)
_ = Task.Run(async () =>
{
    await Task.Delay(5000); // Wait for service discovery
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Running database migrations...");
        await db.Database.MigrateAsync();
        logger.LogInformation("✅ Database migrations completed");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Failed to run migrations");
    }
});

await app.RunAsync();