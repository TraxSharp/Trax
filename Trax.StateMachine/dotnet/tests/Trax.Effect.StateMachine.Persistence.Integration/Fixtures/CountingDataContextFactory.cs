using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectProvider;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// Wraps the registered <see cref="IDataContextProviderFactory"/> and counts the data contexts it hands out, so a
/// test can tell whether something read the database.
/// </summary>
public sealed class CountingDataContextFactory(IDataContextProviderFactory inner)
    : IDataContextProviderFactory
{
    private int _count;

    /// <summary>Data contexts created so far.</summary>
    public int Count => Volatile.Read(ref _count);

    private TaskCompletionSource? _hold;
    private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Holds every data context asked for from now on until <see cref="Release"/>; the task completes when the first
    /// one is asked for, so a test knows a read is waiting.
    /// </summary>
    public Task Hold()
    {
        _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(
            ref _hold,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        );
        return _entered.Task;
    }

    /// <summary>Lets the held data contexts go, and stops holding.</summary>
    public void Release() => Interlocked.Exchange(ref _hold, null)?.TrySetResult();

    public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        if (Volatile.Read(ref _hold) is { } hold)
        {
            _entered.TrySetResult();
            await hold.Task.WaitAsync(cancellationToken);
        }
        return await inner.CreateDbContextAsync(cancellationToken);
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
