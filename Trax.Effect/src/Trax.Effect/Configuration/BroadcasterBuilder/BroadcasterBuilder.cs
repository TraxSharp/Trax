using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Services.EffectRegistry;

namespace Trax.Effect.Configuration.BroadcasterBuilder;

/// <summary>
/// Builder for configuring the cross-process lifecycle event broadcaster.
/// Transport-specific extensions (e.g., <c>UseRabbitMq()</c>) register their
/// <see cref="Trax.Effect.Services.TrainEventBroadcaster.ITrainEventBroadcaster"/> and
/// <see cref="Trax.Effect.Services.TrainEventBroadcaster.ITrainEventReceiver"/> implementations.
/// </summary>
public class BroadcasterBuilder
{
    internal BroadcasterBuilder(TraxEffectBuilder.TraxEffectBuilder parent)
    {
        ServiceCollection = parent.ServiceCollection;
        EffectRegistry = parent.EffectRegistry;
    }

    public IServiceCollection ServiceCollection { get; }

    public IEffectRegistry? EffectRegistry { get; }
}
