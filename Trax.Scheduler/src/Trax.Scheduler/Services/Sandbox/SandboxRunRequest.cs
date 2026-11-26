using System.ComponentModel;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// What a hub asks a sandbox launcher for. The shape is fixed and is all a hub can say: which
/// profile, which train, its input and its limits. It has no field for an image, a command, an
/// environment, a mount, a service account, a runtime, the host network, privileges or
/// capabilities, and the launcher refuses a request carrying any member it does not know.
/// </summary>
/// <param name="RunId">32 lowercase hex characters, chosen by the hub; the launcher names its sandbox after it and echoes it.</param>
/// <param name="Profile">A profile in the launcher's configuration.</param>
/// <param name="TrainName">The canonical name (interface FullName) of a train in the profile's image that carries <see cref="TraxSandboxAttribute"/>.</param>
/// <param name="InputJson">The train's input, as JSON.</param>
/// <param name="Limits">The limits the train declared.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox#the-wire">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SandboxRunRequest(
    string RunId,
    string Profile,
    string TrainName,
    string InputJson,
    SandboxLimits Limits
);
