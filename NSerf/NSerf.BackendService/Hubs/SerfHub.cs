using Microsoft.AspNetCore.SignalR;
using NSerf.Agent;
using NSerf.Serf;

namespace NSerf.BackendService.Hubs;

public class SerfHub : Hub
{
    private readonly SerfAgent _agent;
    private readonly ILogger<SerfHub> _logger;

    public SerfHub(SerfAgent agent, ILogger<SerfHub> logger)
    {
        _agent = agent;
        _logger = logger;
    }

    public async Task SendUserEvent(string eventName, string payload, bool coalesce)
    {
        if (_agent.Serf == null) return;
        
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
        await _agent.Serf.UserEventAsync(eventName, payloadBytes, coalesce);
        
        _logger.LogInformation("Sent user event {EventName} from dashboard", eventName);
        
        // Notify all clients for visual effect (optimistic)
        await Clients.All.SendAsync("EventSent", new { Event = "user", Name = eventName, Payload = payload });
    }

    public async Task SendQuery(string queryName, string payload)
    {
        if (_agent.Serf == null) return;
        
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
        // Fire and forget query for now
        _ = _agent.Serf.QueryAsync(queryName, payloadBytes, new QueryParam());
        
        _logger.LogInformation("Sent query {QueryName} from dashboard", queryName);
        
        // Notify all clients for visual effect
        await Clients.All.SendAsync("EventSent", new { Event = "query", Name = queryName, Payload = payload });
    }
}
