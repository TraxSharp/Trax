using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>
/// Declares who may read an instance, through <c>get</c> or the push (<c>&lt;machine&gt;Changed</c>), or delete one through
/// <c>discard</c>. See <see cref="IMachineBuilder{TState,TTrigger}.Get"/>,
/// <see cref="IMachineBuilder{TState,TTrigger}.Watch"/> and <see cref="IMachineBuilder{TState,TTrigger}.Discard"/>.
/// </summary>
public interface IReadRuleBuilder
{
    /// <summary>Only the instance's owner may read it.</summary>
    IReadRuleBuilder OwnerOnly();

    /// <summary>Only a caller in at least one of <paramref name="roles"/> may read it.</summary>
    IReadRuleBuilder Requires(params string[] roles);

    /// <summary>Only a caller who satisfies the authorization policy <paramref name="policy"/> may read it.</summary>
    IReadRuleBuilder RequiresPolicy(string policy);

    /// <summary>Any authenticated caller may read it.</summary>
    IReadRuleBuilder AnyAuthenticated();

    /// <summary>Any caller may read it, authenticated or not.</summary>
    IReadRuleBuilder Anonymous();
}
