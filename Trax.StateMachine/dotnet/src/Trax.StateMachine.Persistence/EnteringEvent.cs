using System.Diagnostics.CodeAnalysis;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.StateMachine.Persistence;

/// <summary>An event that enters an invoking state, or the start into an invoking initial state.</summary>
/// <param name="Name">The trigger's name, or <see cref="Start"/>.</param>
/// <param name="Rule">The rule the machine declares for it, or <see langword="null"/> when it declares none.</param>
internal sealed record EnteringEvent(string Name, CallerRule? Rule)
{
    /// <summary>The name the machine's start enters under.</summary>
    public const string Start = "Start";
}
