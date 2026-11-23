using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>
/// Declares where the outcome of a state's invoked train goes. Returned by
/// <see cref="IStateBuilder{TState,TTrigger}.Invokes{TTrain,TInput,TOutput}"/>; it is also the state's builder,
/// so the state's other transitions follow it.
///
/// <para>Every invoking state needs one or more <see cref="OnDone"/> edges, exactly one <see cref="OnFailed"/>
/// and exactly one <see cref="OnCancelled"/>; <see cref="MachineBuilder{TState,TTrigger}.Build"/> refuses a
/// state missing either of the last two, naming it. Every target joins the machine's reserved states, and no
/// ordinary transition may enter one.</para>
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/machine#iinvokebuilder">SDK reference</seealso>
public interface IInvokeBuilder<TState, TTrigger> : IStateBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>
    /// When the run succeeds, go to <paramref name="target"/>. Declare several to route on the output: they are
    /// tried in declaration order and the first whose <paramref name="when"/> holds is taken, so an unguarded
    /// edge after the guarded ones is the fallback. To the engine, an output no edge accepts moves nothing (a typed
    /// <c>NO_TRANSITION</c>); on the server the run has finished, so such an output is applied as the state's
    /// <see cref="OnFailed"/>, with the reason <c>invoke-output-unaccepted</c>.
    /// </summary>
    /// <param name="target">The state the machine enters.</param>
    /// <param name="when">
    /// A guard over the train's output, which is this outcome's input: write it with
    /// <c>Rules.Input((MyOutput o) =&gt; o.Unsure).IsTrue()</c>. Null takes the edge for every output.
    /// </param>
    /// <param name="reduce">
    /// How the output becomes the target's context, e.g.
    /// <c>Rules.Set((Ctx c) =&gt; c.Fingerprint).FromInput((MyOutput o) =&gt; o.Fingerprint)</c>. Null keeps the
    /// context. A snapshot holds pointers, not data: reduce a fingerprint or a URI, never rows.
    /// </param>
    IInvokeBuilder<TState, TTrigger> OnDone(
        TState target,
        Rule? when = null,
        Reduction? reduce = null
    );

    /// <summary>
    /// When the run fails, including a run the scheduler reaps, go to <paramref name="target"/>. Declare it
    /// exactly once. The scheduler never retries an invoked run; a machine retries by entering the invoking state
    /// again.
    /// </summary>
    /// <param name="target">The state the machine enters.</param>
    /// <param name="reduce">How the context changes; null keeps it. The outcome carries no input.</param>
    IInvokeBuilder<TState, TTrigger> OnFailed(TState target, Reduction? reduce = null);

    /// <summary>
    /// When the run is cancelled (a timeout, or, for a system-owned instance, an operator's cancel of the
    /// instance, its run or its queued entry), go to <paramref name="target"/>. Operators cannot cancel a
    /// user-owned instance's run: its user does, by leaving the state through one of the machine's own
    /// transitions, which moves the instance itself and so never takes this edge. Required, and declared exactly
    /// once.
    /// </summary>
    /// <param name="target">The state the machine enters.</param>
    /// <param name="reduce">How the context changes; null keeps it. The outcome carries no input.</param>
    IInvokeBuilder<TState, TTrigger> OnCancelled(TState target, Reduction? reduce = null);
}
