using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>
/// Declares a machine's <c>start</c>: how its input record becomes the initial context, and the rule a caller
/// must meet. See <see cref="IMachineBuilder{TState,TTrigger}.Start{TStartInput}"/>.
/// </summary>
/// <typeparam name="TStartInput">The record a client starts an instance with.</typeparam>
public interface IStartBuilder<TStartInput>
{
    /// <summary>Produce the initial context from the start input with a declarative <see cref="Reduction"/>.</summary>
    IStartBuilder<TStartInput> Reduce(Reduction reduce);

    /// <summary>Only a caller in at least one of <paramref name="roles"/> may start an instance.</summary>
    IStartBuilder<TStartInput> Requires(params string[] roles);

    /// <summary>Only a caller who satisfies the authorization policy <paramref name="policy"/> may start an instance.</summary>
    IStartBuilder<TStartInput> RequiresPolicy(string policy);

    /// <summary>Any authenticated caller may start an instance.</summary>
    IStartBuilder<TStartInput> AnyAuthenticated();

    /// <summary>Any caller may start an instance, authenticated or not.</summary>
    IStartBuilder<TStartInput> Anonymous();
}
