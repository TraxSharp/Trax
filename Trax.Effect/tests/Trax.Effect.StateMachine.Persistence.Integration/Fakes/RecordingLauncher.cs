using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// Stands in for the mediator's launcher, which this folder cannot reference: it writes the run's work queue entry
/// into the caller's data context under the external id it is handed, as the mediator does, and records each launch.
/// </summary>
public sealed class RecordingLauncher : IInvokedTrainLauncher
{
    public List<InvokedTrainLaunch> Launches { get; } = [];

    public IReadOnlyList<string> Refusals(
        InvokedTrainDeclaration declaration,
        IServiceProvider services
    ) => [];

    public async Task Launch(
        InvokedTrainLaunch launch,
        IDataContext context,
        CancellationToken cancellationToken = default
    )
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = launch.TrainType.FullName!,
                Input = "{}",
                InputTypeName = launch.Input.GetType().FullName,
                InvokedBy = launch.InvokedBy,
            }
        );
        entry.ExternalId = launch.ExternalId;
        await context.Track(entry);
        await context.SaveChanges(cancellationToken);
        Launches.Add(launch);
    }
}
