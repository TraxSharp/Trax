using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectProvider;

namespace Trax.Api.Tests.PersistedOperations.Fixtures;

/// <summary>
/// Wraps the registered <see cref="IDataContextProviderFactory"/> and counts the data contexts it
/// hands out, so a test can tell whether a request read the database.
/// </summary>
public sealed class CountingDataContextFactory(IDataContextProviderFactory inner)
    : IDataContextProviderFactory
{
    private int _count;

    /// <summary>Data contexts created so far.</summary>
    public int Count => Volatile.Read(ref _count);

    public Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        return inner.CreateDbContextAsync(cancellationToken);
    }

    public IEffectProvider Create()
    {
        Interlocked.Increment(ref _count);
        return inner.Create();
    }

    /// <summary>
    /// Replaces the registered factory with a counting one, itself resolvable as
    /// <see cref="CountingDataContextFactory"/>.
    /// </summary>
    public static void Install(IServiceCollection services)
    {
        var original = services.Last(d => d.ServiceType == typeof(IDataContextProviderFactory));
        services.Remove(original);
        services.AddSingleton(sp => new CountingDataContextFactory(Resolve(sp, original)));
        services.AddSingleton<IDataContextProviderFactory>(sp =>
            sp.GetRequiredService<CountingDataContextFactory>()
        );
    }

    private static IDataContextProviderFactory Resolve(
        IServiceProvider sp,
        ServiceDescriptor descriptor
    ) =>
        (IDataContextProviderFactory)(
            descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(sp)
            ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!)
        );
}
