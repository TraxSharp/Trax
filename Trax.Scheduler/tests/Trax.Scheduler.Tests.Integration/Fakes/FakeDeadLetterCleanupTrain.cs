using LanguageExt;
using Trax.Effect.Models.Metadata;
using Trax.Scheduler.Trains.DeadLetterCleanup;

namespace Trax.Scheduler.Tests.Integration.Fakes;

/// <summary>
/// Test double for the internal <see cref="IDeadLetterCleanupTrain"/>. NSubstitute cannot proxy an
/// internal interface without granting Trax.Scheduler's internals to its proxy assembly, so the
/// polling-service tests count runs with this instead.
/// </summary>
internal sealed class FakeDeadLetterCleanupTrain(Action? onRun = null) : IDeadLetterCleanupTrain
{
    private int _runs;

    public int Runs => Volatile.Read(ref _runs);

    public Metadata? Metadata => null;

    public Task<Unit> Run(
        DeadLetterCleanupRequest input,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref _runs);
        onRun?.Invoke();
        return Task.FromResult(Unit.Default);
    }

    public void Dispose() { }
}
