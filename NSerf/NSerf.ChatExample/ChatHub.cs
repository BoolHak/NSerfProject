using Microsoft.AspNetCore.SignalR;
using NSerf.Agent;
using NSerf.Serf.Events;
using System.Text;
using System.Text.Json;

namespace NSerf.ChatExample;

/// <summary>
/// SignalR hub that integrates with Serf for distributed chat messaging.
/// Messages sent to one server instance are automatically broadcast to all other instances via Serf.
/// </summary>
public class ChatHub : Hub, IEventHandler
{
    private readonly SerfAgent _agent;
    private readonly ILogger<ChatHub> _logger;
    private readonly IHubContext<ChatHub> _hubContext;
    private static readonly object _initLock = new();
    private static bool _initialized;

    public ChatHub(SerfAgent agent, ILogger<ChatHub> logger, IHubContext<ChatHub> hubContext)
    {
        _agent = agent;
        _logger = logger;
        _hubContext = hubContext;

        // Register this hub as a Serf event handler (only once)
        lock (_initLock)
        {
            if (!_initialized)
            {
                _agent.RegisterEventHandler(this);
                _initialized = true;
                _logger.LogInformation("[ChatHub] Registered as Serf event handler");
            }
        }
    }

    /// <summary>
    /// Called when a client sends a chat message.
    /// Broadcasts to local clients and sends via Serf to other cluster nodes.
    /// </summary>
    public async Task SendMessage(string user, string message)
    {
        _logger.LogInformation("[ChatHub] Message from {User}: {Message}", user, message);

        // Broadcast to all clients connected to THIS server
        await Clients.All.SendAsync("ReceiveMessage", user, message, _agent.NodeName);

        // Broadcast to other Serf cluster nodes via user event
        if (_agent.Serf != null)
        {
            var chatMessage = new ChatMessage
            {
                User = user,
                Message = message,
                SourceNode = _agent.NodeName,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(chatMessage));
            
            try
            {
                await _agent.Serf.UserEventAsync("chat-message", payload, coalesce: false);
                _logger.LogDebug("[ChatHub] Broadcast message to cluster via Serf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatHub] Failed to broadcast via Serf");
            }
        }
    }

    /// <summary>
    /// Handles Serf events - receives chat messages from other nodes.
    /// </summary>
    public void HandleEvent(Event @event)
    {
        try
        {
            // Only process user events with name "chat-message"
            if (@event is UserEvent userEvent && userEvent.Name == "chat-message")
            {
                var json = Encoding.UTF8.GetString(userEvent.Payload);
                var chatMessage = JsonSerializer.Deserialize<ChatMessage>(json);

                if (chatMessage != null)
                {
                    // Ignore messages from our own node (already displayed locally)
                    if (chatMessage.SourceNode == _agent.NodeName)
                        return;

                    _logger.LogInformation("[ChatHub] Received message from {Node}: {User}: {Message}",
                        chatMessage.SourceNode, chatMessage.User, chatMessage.Message);

                    // Broadcast to all clients connected to THIS server
                    _ = _hubContext.Clients.All.SendAsync(
                        "ReceiveMessage",
                        chatMessage.User,
                        chatMessage.Message,
                        chatMessage.SourceNode);
                }
            }
            else if (@event is MemberEvent memberEvent)
            {
                // Notify clients about cluster membership changes
                var eventType = memberEvent.Type.ToString().ToLower();
                var members = string.Join(", ", memberEvent.Members.Select(m => m.Name));
                
                _ = _hubContext.Clients.All.SendAsync(
                    "SystemMessage",
                    $"[Cluster {eventType}] {members}");
                
                _logger.LogInformation("[ChatHub] Member event: {Type} - {Members}", eventType, members);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatHub] Error handling Serf event");
        }
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("[ChatHub] Client connected: {ConnectionId}", Context.ConnectionId);
        
        // Send cluster info to the new client
        if (_agent.Serf != null)
        {
            var memberCount = _agent.Serf.Members().Length;
            _ = Clients.Caller.SendAsync("SystemMessage", 
                $"Connected to {_agent.NodeName} (cluster size: {memberCount})");
        }
        
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("[ChatHub] Client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Message format for chat messages broadcast via Serf.
/// </summary>
public class ChatMessage
{
    public string User { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SourceNode { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}
