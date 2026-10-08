using System.Diagnostics.CodeAnalysis;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Queues the run of a train a machine state invokes, through the caller's own data context so the run's work
/// queue entry commits in the transaction that moves the snapshot into the state, or not at all. Trax.Mediator
/// implements it (<c>AddMediator</c> registers it), so the enqueue goes through the mediator: the train is resolved
/// by its canonical name, authorized, its input capped and its subject key applied as any caller's enqueue is. A
/// host that declares <c>Invokes</c> without registering one is refused at startup.
/// </summary>
/// <remarks>
/// It lives in the persistence package rather than the engine because it writes through
/// <see cref="IDataContext"/>, and the engine package depends on no data provider. Infrastructure: a host does not
/// implement or call it.
/// </remarks>
[Experimental(ExperimentalIds.Invokes)]
public interface IInvokedTrainLauncher
{
    /// <summary>
    /// What stops <paramref name="declaration"/>'s train from being invoked, one sentence per problem, each naming
    /// the machine, the state and the train, the junction or the member at fault; empty when nothing does. The
    /// state-machine startup check calls this for every invoking state and refuses the host when any is returned.
    /// </summary>
    /// <param name="declaration">The invoking state and the train it names.</param>
    /// <param name="services">The host's container, read for the train's registration and its junctions.</param>
    IReadOnlyList<string> Refusals(InvokedTrainDeclaration declaration, IServiceProvider services);

    /// <summary>
    /// Authorizes the run and writes its work queue entry into <paramref name="context"/> under
    /// <see cref="InvokedTrainLaunch.ExternalId"/>, flushing it inside whatever transaction the caller holds open
    /// and committing nothing itself. A user-owned instance's run is authorized against the current caller, the
    /// user entering the state; a system-owned instance's run is authorized inside Trax's trusted execution scope,
    /// as a scheduled manifest run is.
    /// </summary>
    /// <param name="launch">The train, its input, the id its entry takes, and the instance invoking it.</param>
    /// <param name="context">The data context, and transaction, the snapshot is written through.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="UnauthorizedAccessException">The current caller may not run the train.</exception>
    Task Launch(
        InvokedTrainLaunch launch,
        IDataContext context,
        CancellationToken cancellationToken = default
    );
}

/// <summary>A machine state that invokes a train, as the startup check describes it to the launcher.</summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="State">The invoking state.</param>
/// <param name="TrainType">The train's interface, its canonical name.</param>
/// <param name="InputType">The train's input type, as the state declared it.</param>
/// <param name="OutputType">The train's output type, as the state declared it.</param>
/// <param name="SystemOwned">Whether the machine's instances belong to the system rather than to users.</param>
[Experimental(ExperimentalIds.Invokes)]
public sealed record InvokedTrainDeclaration(
    string Machine,
    string State,
    Type TrainType,
    Type InputType,
    Type OutputType,
    bool SystemOwned
);

/// <summary>One run an invoking state queues.</summary>
/// <param name="TrainType">The train's interface, its canonical name.</param>
/// <param name="Input">The run's input, built from the context the state was entered with.</param>
/// <param name="ExternalId">The id the run's work queue entry takes, which the instance stores as its invoke token.</param>
/// <param name="InvokedBy">The instance invoking it, written on the entry and carried to the run.</param>
[Experimental(ExperimentalIds.Invokes)]
public sealed record InvokedTrainLaunch(
    Type TrainType,
    object Input,
    string ExternalId,
    InvokedBy InvokedBy
);
