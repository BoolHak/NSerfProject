using NSerf.Serf.Events;

namespace NSerf.Agent;

/// <summary>
/// Interface for handling Serf events.
/// Ported from Go: EventHandler interface
/// </summary>
public interface IEventHandler
{
    /// <summary>
    /// Handle a Serf event asynchronously.
    /// </summary>
    /// <param name="evt">The Serf event to handle</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task HandleEventAsync(Event evt, CancellationToken cancellationToken = default);
}
