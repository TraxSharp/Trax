using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>An outcome edge of an invoking state that enters another invoking state.</summary>
/// <param name="State">The invoking state the run belongs to.</param>
/// <param name="Outcome">The outcome, <c>OnDone</c>, <c>OnFailed</c> or <c>OnCancelled</c>.</param>
/// <param name="Target">The state the outcome enters, which invokes a train of its own.</param>
internal sealed record ChainedOutcome(string State, string Outcome, string Target);
