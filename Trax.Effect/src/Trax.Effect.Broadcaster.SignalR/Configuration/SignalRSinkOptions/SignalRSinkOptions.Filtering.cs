namespace Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions;

public partial class SignalRSinkOptions
{
    /// <summary>
    /// Restrict the sink to events whose <c>EventType</c> matches one of the listed values
    /// (e.g. "Started", "Completed", "Failed", "Cancelled", "StateChanged").
    /// Calling this multiple times accumulates. With no call, every event type is allowed.
    /// </summary>
    public SignalRSinkOptions OnlyForEvents(params string[] eventTypes)
    {
        if (eventTypes is null || eventTypes.Length == 0)
        {
            throw new ArgumentException(
                "OnlyForEvents() requires at least one event type. "
                    + "Pass values like \"Completed\", \"Failed\", or omit the call to allow every event type.",
                nameof(eventTypes)
            );
        }

        foreach (var eventType in eventTypes)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                throw new ArgumentException(
                    "OnlyForEvents() does not accept null or whitespace event types.",
                    nameof(eventTypes)
                );
            }

            _eventTypes.Add(eventType);
        }

        return this;
    }

    /// <summary>
    /// Restrict the sink to events whose <c>TrainName</c> matches one of the supplied train interface
    /// types' <c>FullName</c>. Pass interface types (e.g. <c>typeof(IMyTrain)</c>); each entry stores
    /// <c>type.FullName</c>, which is the canonical identifier Trax uses on the wire.
    /// </summary>
    public SignalRSinkOptions OnlyForTrains(params Type[] trainInterfaceTypes)
    {
        if (trainInterfaceTypes is null || trainInterfaceTypes.Length == 0)
        {
            throw new ArgumentException(
                "OnlyForTrains() requires at least one type. "
                    + "Pass train interface types (e.g. typeof(IMyTrain)), "
                    + "or omit the call to allow every train.",
                nameof(trainInterfaceTypes)
            );
        }

        foreach (var type in trainInterfaceTypes)
        {
            if (type is null)
            {
                throw new ArgumentException(
                    "OnlyForTrains() does not accept null type entries.",
                    nameof(trainInterfaceTypes)
                );
            }

            if (!type.IsInterface)
            {
                throw new ArgumentException(
                    $"OnlyForTrains() expects train interface types but received concrete type {type.FullName}. "
                        + "Trax stores the train interface FullName as the canonical identifier on the wire, "
                        + "so the filter must match interfaces.",
                    nameof(trainInterfaceTypes)
                );
            }

            _trainNames.Add(type.FullName!);
        }

        return this;
    }

    /// <inheritdoc cref="OnlyForTrains(Type[])"/>
    public SignalRSinkOptions OnlyForTrains<T1>()
        where T1 : class => OnlyForTrains(typeof(T1));

    /// <inheritdoc cref="OnlyForTrains(Type[])"/>
    public SignalRSinkOptions OnlyForTrains<T1, T2>()
        where T1 : class
        where T2 : class => OnlyForTrains(typeof(T1), typeof(T2));

    /// <inheritdoc cref="OnlyForTrains(Type[])"/>
    public SignalRSinkOptions OnlyForTrains<T1, T2, T3>()
        where T1 : class
        where T2 : class
        where T3 : class => OnlyForTrains(typeof(T1), typeof(T2), typeof(T3));
}
