using System.Diagnostics.CodeAnalysis;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Queues the run of a train a machine state invokes, through the data context it is handed (the outbox's) so the
/// run's work queue entry commits in the transaction that moves the snapshot into the state, or not at all. Trax.Mediator
/// implements it (<c>AddMediator</c> registers it), so the enqueue goes through the mediator: the train is resolved
/// by its canonical name, authorized, its input capped and its subject key applied as any caller's enqueue is. A
/// host that declares <c>Invokes</c> without registering one is refused at startup.
/// </summary>
/// <remarks>
/// It lives in the persistence package rather than the engine because it writes through
/// <see cref="IDataContext"/>, and the engine package depends on no data provider. Infrastructure: a host does not
/// implement or call it.
/// </remarks>
internal interface IInvokedTrainLauncher
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
internal sealed record InvokedTrainDeclaration(
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
/// <param name="State">The invoking state being entered, named in a refusal.</param>
internal sealed record InvokedTrainLaunch(
    Type TrainType,
    object Input,
    string ExternalId,
    InvokedBy InvokedBy,
    string State
)
{
    /// <summary>
    /// True when the state was entered by the outcome of the run before it rather than by a caller: an
    /// <c>OnDone</c>, <c>OnFailed</c> or <c>OnCancelled</c> target that itself invokes a train. No user is present
    /// to authorize against, so only a system-owned instance may launch one, in Trax's trusted execution scope.
    /// The startup check refuses a user-owned machine whose outcome enters an invoking state, and the launcher
    /// refuses such a launch for a user-owned instance with <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public bool FromOutcome { get; init; }
}

/// <summary>
/// The refusals the startup check makes and a launch makes again at runtime, worded once, so a run refused at entry
/// says what the startup check would have said.
/// </summary>
internal static class InvokeRefusals
{
    /// <summary>Where a refusal is: the machine, the train and the state that invokes it.</summary>
    public static string At(string machine, string state, Type train) =>
        $"The machine '{machine}' invokes {train.Name} in {state}";

    /// <summary>A train whose output reaches a <c>[TraxSensitive]</c> member, refused for every owner.</summary>
    public static string SensitiveOutput(string at, Type output) =>
        $"{at}, whose output {output.Name} reaches a [TraxSensitive] member. The output is reduced into the "
        + "snapshot's context, which is stored as plain JSON and returned by loadSnapshot, so it cannot hold a "
        + "sensitive value. Return a pointer to it instead.";

    /// <summary>A <c>[TraxBroadcast]</c> train, refused for a user-owned machine.</summary>
    public static string Broadcast(string at) =>
        $"{at}, which is [TraxBroadcast]. Its subscribers see every run's output, so a user-owned machine's run "
        + "would be broadcast to others; invoke a train that is not broadcast.";
}
