using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// The one-shot gate a <see cref="PausingSnapshotStore"/> holds a write at. A test arms it, starts the call, awaits
/// <see cref="Reached"/>, looks at the database, and releases it.
/// </summary>
public sealed class WriteGate
{
    private readonly TaskCompletionSource _reached = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource _released = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private PausePoint? _armed;

    /// <summary>Holds the next write at <paramref name="point"/>.</summary>
    public void Arm(PausePoint point) => _armed = point;

    /// <summary>Completes when a write is held.</summary>
    public Task Reached => _reached.Task;

    /// <summary>Lets the held write go on.</summary>
    public void Release() => _released.TrySetResult();

    internal async Task At(PausePoint point)
    {
        if (_armed != point)
            return;
        _armed = null;
        _reached.TrySetResult();
        await _released.Task.ConfigureAwait(false);
    }

    /// <summary>Replaces a host's <see cref="ISnapshotStore"/> with one that holds its writes at <paramref name="gate"/>.</summary>
    public static Action<IServiceCollection> Install(WriteGate gate) =>
        services =>
            services.AddScoped<ISnapshotStore>(sp => new PausingSnapshotStore(
                new EfSnapshotStore(
                    sp.GetRequiredService<IDataContext>(),
                    sp.GetService<ISqlDialect>()
                ),
                gate
            ));
}
