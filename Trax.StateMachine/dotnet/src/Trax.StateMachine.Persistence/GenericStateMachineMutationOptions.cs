using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>The options of <see cref="StateMachineOptions.ExposeGenericStateMachineMutations"/>.</summary>
public sealed class GenericStateMachineMutationOptions
{
    /// <summary>
    /// The authorization policy a caller must satisfy to reach any of the six generic mutations. Required: the
    /// opt-in without one refuses to start. The host registers the policy with ASP.NET Core authorization as it
    /// does for a train's <c>[TraxAuthorize(Policy = ...)]</c>.
    /// </summary>
    public string? Policy { get; set; }
}
