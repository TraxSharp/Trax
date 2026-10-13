using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Postgres.Services.PostgresContext;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Counts the Postgres data contexts a host creates, so a test can tell how often a node read the database while it
/// did one thing. Every path to the database (the scoped data context, the provider factory's contexts) goes through
/// EF's context factory, which this wraps. Modelled on the persisted-operations tests' counting factory.
/// </summary>
public sealed class CountingDataContexts
{
    private int _count;

    /// <summary>Data contexts created so far.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>
    /// Replaces the host's context factory with a counting one, and registers the counter. Pass it as
    /// <see cref="E2EHostOptions.Services"/>, which runs after Trax's registrations.
    /// </summary>
    public static void Install(IServiceCollection services)
    {
        var counter = new CountingDataContexts();
        services.AddSingleton(counter);
        var contexts = services.Last(d =>
            d.ServiceType == typeof(IDbContextFactory<PostgresContext>)
        );
        services.Remove(contexts);
        services.Add(
            new ServiceDescriptor(
                typeof(IDbContextFactory<PostgresContext>),
                sp => new CountingContextFactory(
                    (IDbContextFactory<PostgresContext>)(
                        contexts.ImplementationInstance
                        ?? contexts.ImplementationFactory?.Invoke(sp)
                        ?? ActivatorUtilities.CreateInstance(sp, contexts.ImplementationType!)
                    ),
                    counter
                ),
                contexts.Lifetime
            )
        );
    }

    private sealed class CountingContextFactory(
        IDbContextFactory<PostgresContext> inner,
        CountingDataContexts counter
    ) : IDbContextFactory<PostgresContext>
    {
        public PostgresContext CreateDbContext()
        {
            Interlocked.Increment(ref counter._count);
            return inner.CreateDbContext();
        }
    }
}

/// <summary>A clock a test moves by hand.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
