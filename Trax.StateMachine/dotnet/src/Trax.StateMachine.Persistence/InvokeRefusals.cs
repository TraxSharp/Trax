using System.Diagnostics.CodeAnalysis;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.StateMachine.Persistence;

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
