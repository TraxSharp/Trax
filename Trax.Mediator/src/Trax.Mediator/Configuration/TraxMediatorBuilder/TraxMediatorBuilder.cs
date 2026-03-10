using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxBuilder;

namespace Trax.Mediator.Configuration;

/// <summary>
/// Builder for configuring the Trax mediator system (train bus, train discovery, assembly scanning).
/// </summary>
public partial class TraxMediatorBuilder
{
    private readonly TraxBuilderWithEffects _parent;
    private ServiceLifetime _lifetime = ServiceLifetime.Transient;
    private readonly List<Assembly> _assemblies = [];

    internal TraxMediatorBuilder(TraxBuilderWithEffects parent)
    {
        _parent = parent;
    }
}
