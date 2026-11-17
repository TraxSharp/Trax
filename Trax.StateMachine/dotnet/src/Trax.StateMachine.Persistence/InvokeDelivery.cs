using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;

namespace Trax.StateMachine.Persistence;

/// <summary>What one delivery of an invoked run's outcome did.</summary>
internal abstract record InvokeDelivery
{
    /// <summary>The outcome moved the instance: the conditional update matched, once.</summary>
    /// <param name="Machine">The machine.</param>
    /// <param name="Id">The instance.</param>
    /// <param name="From">The invoking state the run belonged to.</param>
    /// <param name="To">The state the outcome entered.</param>
    /// <param name="Applied">The outcome applied, <c>done</c>, <c>failed</c> or <c>cancelled</c>: the run's own, or <c>failed</c> when it could not be.</param>
    /// <param name="Reason">Why a run's own outcome was applied as Failed, one of <see cref="InvokeOutcomeReasons"/>; null otherwise.</param>
    /// <param name="NextToken">The run the entered state queued, when it invokes a train.</param>
    public sealed record Moved(
        string Machine,
        Guid Id,
        string From,
        string To,
        string Applied,
        string? Reason,
        string? NextToken
    ) : InvokeDelivery;

    /// <summary>
    /// No row holds the run's token: the outcome was applied already, or the state it belonged to was left (by a
    /// declared transition, which cancelled the run). Nothing is written. A typed no-transition.
    /// </summary>
    public sealed record NoTransition : InvokeDelivery;

    /// <summary>The run has not ended. Nothing is written.</summary>
    public sealed record Running : InvokeDelivery;

    /// <summary>The row belongs to a machine this host does not register, or cannot read. Another host applies it.</summary>
    /// <param name="Machine">The row's machine.</param>
    public sealed record NotHere(string Machine) : InvokeDelivery;

    /// <summary>The row kept changing under every attempt; the next sweep tries again.</summary>
    public sealed record Contended : InvokeDelivery;

    /// <summary>
    /// Not even the run's failure could be applied (the <c>OnFailed</c> or <c>OnCancelled</c> edge's reduction
    /// is refused, or its target's run cannot be queued). The token is cleared and the state recorded as stranded
    /// (<c>invoke_stranded_state</c>), so the instance stays in the state with no live run and leaves it only
    /// through one of its declared transitions; the reason is logged.
    /// </summary>
    /// <param name="Machine">The machine.</param>
    /// <param name="Id">The instance.</param>
    /// <param name="State">The state it stays in.</param>
    /// <param name="Reason">One of <see cref="InvokeOutcomeReasons"/>.</param>
    public sealed record Stranded(string Machine, Guid Id, string State, string Reason)
        : InvokeDelivery;

    private InvokeDelivery() { }
}
