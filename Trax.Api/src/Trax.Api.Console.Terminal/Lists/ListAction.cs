using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>
/// Something an operator does to rows of a list, through one of the console's documents. The host checks it as it
/// checks the browser console's: the operations gate decides, and the answer says what happened.
/// </summary>
/// <param name="Name">The action's name on the command line (<c>do dead-letters requeue 7</c>).</param>
/// <param name="Key">The key that runs it on the selected rows in the terminal.</param>
/// <param name="Description">What it does, for the help line.</param>
/// <param name="NeedsNote">Whether it asks for a note, which the host records with it.</param>
/// <param name="Confirm">Whether the terminal asks before running it.</param>
/// <param name="Run">Sends it for the rows' ids.</param>
/// <param name="AsksAfresh">Whether the run it queues may ask its decisions afresh rather than replay them.</param>
internal sealed record ListAction<TRow>(
    string Name,
    char Key,
    string Description,
    bool NeedsNote,
    bool Confirm,
    Func<
        TraxClient,
        IReadOnlyList<long>,
        ActionOptions,
        CancellationToken,
        Task<ActionOutcome>
    > Run,
    bool AsksAfresh = false
);
