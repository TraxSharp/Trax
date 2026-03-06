using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Mediator.Extensions;

namespace Trax.Mediator.Configuration;

/// <summary>
/// Builder for configuring the Trax mediator system (train bus, train discovery, assembly scanning).
/// </summary>
public class TraxMediatorBuilder
{
    private readonly TraxBuilderWithEffects _parent;
    private ServiceLifetime _lifetime = ServiceLifetime.Transient;
    private readonly List<Assembly> _assemblies = [];

    internal TraxMediatorBuilder(TraxBuilderWithEffects parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Adds assemblies to scan for <c>IServiceTrain&lt;TIn, TOut&gt;</c> implementations.
    /// </summary>
    public TraxMediatorBuilder ScanAssemblies(params Assembly[] assemblies)
    {
        _assemblies.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Sets the DI lifetime for discovered train implementations.
    /// </summary>
    /// <param name="lifetime">The service lifetime (default: Transient)</param>
    public TraxMediatorBuilder TrainLifetime(ServiceLifetime lifetime)
    {
        _lifetime = lifetime;
        return this;
    }

    internal void Build()
    {
        _parent.ServiceCollection.AddServiceTrainBus(_lifetime, [.. _assemblies]);
    }
}
