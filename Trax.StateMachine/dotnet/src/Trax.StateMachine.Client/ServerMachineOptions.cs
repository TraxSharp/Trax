using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>How a <see cref="ServerMachineController"/> is built.</summary>
public sealed record ServerMachineOptions
{
    /// <summary>The machine as the host's client manifest lists it: its id, IR and IR hash.</summary>
    public required ClientManifestMachine Machine { get; init; }

    /// <summary>The server's operations and change stream for that machine.</summary>
    public required IMachineServer Server { get; init; }

    /// <summary>
    /// The engine to predict with (<see cref="MachinePredictor.For{TState,TTrigger}"/>). Without one, an event is sent
    /// with no prediction and the view shows only what the server confirmed. Its IR hash must be the manifest's.
    /// </summary>
    public IMachinePredictor? Predictor { get; init; }

    /// <summary>An existing instance to open. Without it, <see cref="ServerMachineController.StartAsync"/> creates one.</summary>
    public string? Id { get; init; }

    /// <summary>The start's input, when the machine's start takes one.</summary>
    public JsonNode? StartInput { get; init; }

    /// <summary>
    /// The request id of the start, minted when null. A controller that replaces a disposed one passes the same id, so
    /// the server answers the retried start with the instance it already created.
    /// </summary>
    public string? StartRequestId { get; init; }

    /// <summary>
    /// Mints a request id; a new GUID by default. A custom one must mint 1 to 200 characters, not blank, with no control
    /// character: the server answers any other <c>INVALID_REQUEST_ID</c>. One that throws fails what it was minting for
    /// (a send, or the start) <c>INVALID_REQUEST_ID</c>, sends nothing for it, and the controller carries on.
    /// </summary>
    public Func<string>? NewRequestId { get; init; }

    /// <summary>
    /// Delays between retries of a <c>SERVER_OUTDATED</c> answer (another node of a rolling deploy answered) and of a read
    /// the transport failed. After the last, the event fails and <see cref="ServerMachineController.RetryAsync"/> resends it.
    /// </summary>
    public IReadOnlyList<TimeSpan>? RetryDelays { get; init; }

    // The clock retry delays are waited on; the system's when null. A test pins it.
    internal TimeProvider? Time { get; init; }
}
