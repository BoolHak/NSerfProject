using NSerf.ServiceDiscovery;

namespace NSerf.ToDoExample.Background;

/// <summary>
/// Hosted service to wire NSerf events to ServiceRegistry for service discovery
/// </summary>
public class ServiceDiscoveryHostedService(
    System.IServiceProvider serviceProvider,
    IServiceRegistry registry,
    ILogger<ServiceDiscoveryHostedService> logger)
    : IHostedService
{
    private NSerfServiceProvider? _provider;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("📡 Starting service discovery provider");

        // Wait a bit for SerfHostedService to start and create the Serf instance
        await Task.Delay(2000, cancellationToken);

        try
        {
            var serf = serviceProvider.GetRequiredService<NSerf.Serf.Serf>();
            
            _provider = new NSerfServiceProvider(serf);
            _provider.ServiceDiscovered += async (_, e) =>
            {
                logger.LogInformation(
                    "Service event: {ChangeType} - {ServiceName}/{InstanceId}",
                    e.ChangeType, e.ServiceName, e.Instance?.Id);

                switch (e.ChangeType)
                {
                    case ServiceChangeType.InstanceRegistered:
                    {
                        if (e.Instance != null) await registry.RegisterInstanceAsync(e.Instance, cancellationToken);
                        break;
                    }
                    case ServiceChangeType.InstanceDeregistered:
                        if (e.Instance?.Id != null)
                            await registry.DeregisterInstanceAsync(e.ServiceName, e.Instance.Id, cancellationToken);
                        break;
                    case ServiceChangeType.InstanceUpdated:
                        if (e.Instance != null) await registry.RegisterInstanceAsync(e.Instance, cancellationToken);
                        break;
                    case ServiceChangeType.HealthStatusChanged:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            };

            await _provider.StartAsync(cancellationToken);
            logger.LogInformation("✅ Service discovery provider started");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start service discovery provider");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_provider != null)
        {
            await _provider.StopAsync(cancellationToken);
            logger.LogInformation("Service discovery provider stopped");
        }
    }
}