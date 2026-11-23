using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// A draft service the registry hands its machine's effect runner, which <see cref="ISnapshotDraftService.Send"/>
/// runs the effect through.
/// </summary>
internal interface IEffectRunnerSlot
{
    /// <summary>Resolves the machine's effect runner when a send needs it.</summary>
    Func<ISnapshotEffectRunner?>? EffectRunner { get; set; }
}
