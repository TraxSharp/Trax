using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Mediator.Configuration;

public partial class TraxMediatorBuilder
{
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
}
