using System.Text.Json.Nodes;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A launcher whose enqueue throws for <see cref="QueueingTimerMachine"/>, as the mediator's does for an input it
/// cannot store, and records every other launch.
/// </summary>
internal sealed class ThrowingLauncher : IInvokedTrainLauncher
{
    private readonly RecordingLauncher _inner = new();

    public int Throws => Volatile.Read(ref _throws);

    private int _throws;

    /// <summary>What the enqueue throws; an <see cref="InvalidOperationException"/> unless a test sets it.</summary>
    public Func<Exception> Failure { get; set; } =
        () => new InvalidOperationException("The run's input could not be stored.");

    public IReadOnlyList<string> Refusals(
        InvokedTrainDeclaration declaration,
        IServiceProvider services
    ) => [];

    public Task Launch(
        InvokedTrainLaunch launch,
        IDataContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !string.Equals(
                launch.InvokedBy.Machine,
                QueueingTimerMachine.Id,
                StringComparison.Ordinal
            )
        )
            return _inner.Launch(launch, context, cancellationToken);
        Interlocked.Increment(ref _throws);
        throw Failure();
    }
}
