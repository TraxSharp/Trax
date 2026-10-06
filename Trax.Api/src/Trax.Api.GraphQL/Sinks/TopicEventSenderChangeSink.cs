using HotChocolate.Subscriptions;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Services.ChangeSignal;

namespace Trax.Api.GraphQL.Sinks;

/// <summary>
/// <see cref="IChangeSignalSink"/> that publishes coalesced change signals to the in-process
/// <c>onDataChanged</c> subscription via HotChocolate's <see cref="ITopicEventSender"/>. This is the
/// local delivery path: any WebSocket client subscribed to <c>onDataChanged</c> on this process
/// receives one event per changed domain.
/// </summary>
internal sealed class TopicEventSenderChangeSink : IChangeSignalSink
{
    private readonly ITopicEventSender _eventSender;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the sink. Registered by <c>AddTraxGraphQL</c>; not intended to be constructed directly.</summary>
    /// <param name="eventSender">HotChocolate's subscription transport.</param>
    /// <param name="timeProvider">Stamps each event's timestamp.</param>
    public TopicEventSenderChangeSink(ITopicEventSender eventSender, TimeProvider timeProvider)
    {
        _eventSender = eventSender;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task FlushAsync(IReadOnlyCollection<ChangeDomain> domains, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var domain in domains)
        {
            await _eventSender.SendAsync(
                nameof(LifecycleSubscriptions.OnDataChanged),
                new DataChangedEvent(domain, now),
                ct
            );
        }
    }
}
