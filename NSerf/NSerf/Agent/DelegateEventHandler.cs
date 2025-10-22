using NSerf.Serf.Events;

namespace NSerf.Agent;

/// <summary>
/// Event handler that invokes a delegate/lambda for each event.
/// Useful for quick event handling without creating a full class.
/// </summary>
public class DelegateEventHandler : IEventHandler
{
    private readonly Func<Event, CancellationToken, Task> _handler;

    /// <summary>
    /// Creates a handler that invokes an async delegate.
    /// </summary>
    public DelegateEventHandler(Func<Event, CancellationToken, Task> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Creates a handler that invokes a synchronous delegate.
    /// </summary>
    public DelegateEventHandler(Action<Event> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = (evt, _) =>
        {
            handler(evt);
            return Task.CompletedTask;
        };
    }

    public Task HandleEventAsync(Event evt, CancellationToken cancellationToken = default)
    {
        return _handler(evt, cancellationToken);
    }
}
