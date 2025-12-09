using Microsoft.AspNetCore.SignalR;
using NSerf.Agent;
using NSerf.BackendService.Hubs;
using NSerf.Serf.Events;

namespace NSerf.BackendService.Services;

public class DashboardEventHandler : IHostedService
{
    private readonly SerfAgent _agent;
    private readonly IHubContext<SerfHub> _hubContext;
    private readonly ILogger<DashboardEventHandler> _logger;
    private readonly NetworkTrafficMonitor _monitor;

    public DashboardEventHandler(SerfAgent agent, IHubContext<SerfHub> hubContext, ILogger<DashboardEventHandler> logger, NetworkTrafficMonitor monitor)
    {
        _agent = agent;
        _hubContext = hubContext;
        _logger = logger;
        _monitor = monitor;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _agent.EventReceived += OnSerfEvent;
        _monitor.OnTrafficDetected += OnNetworkActivity;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _agent.EventReceived -= OnSerfEvent;
        _monitor.OnTrafficDetected -= OnNetworkActivity;
        return Task.CompletedTask;
    }

    private void OnNetworkActivity(string type, string targetNode, int bytes)
    {
        _logger.LogInformation("[Dashboard] Broadcasting NetworkTraffic: {Type} -> {Target}", type, targetNode);
        _ = _hubContext.Clients.All.SendAsync("NetworkTraffic", new { type = type, target = targetNode, bytes = bytes });
    }

    private void OnSerfEvent(IEvent evt)
    {
        try
        {
            var type = evt.EventType().ToString().ToLower();
            string name = "";
            string payload = "";

            if (evt is UserEvent userEvent)
            {
                name = userEvent.Name;
                payload = System.Text.Encoding.UTF8.GetString(userEvent.Payload);
            }
            else if (evt is Query query)
            {
                name = query.Name;
                payload = System.Text.Encoding.UTF8.GetString(query.Payload);
            }
            else
            {
                // Member events (Join/Leave/etc)
                // We transmit them so the frontend can animate "gossip" activity or node status changes
                name = type;
                
                // For MemberEvents, we might want to send the affected member name as payload or extra data
                // But for now, just sending the event type allows for generic "activity" visualization
                if (evt is MemberEvent memberEvt)
                {
                    // Serialize member list for detailed handling if needed, 
                    // or just send the first member name for simple viz
                    var member = memberEvt.Members.FirstOrDefault();
                    if (member != null)
                    {
                        payload = member.Name; // Send affected member name as payload
                    }
                }
            }

            // Fire and forget
            _ = _hubContext.Clients.All.SendAsync("EventSent", new { Event = type, Name = name, Payload = payload });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Serf event for dashboard");
        }
    }
}
