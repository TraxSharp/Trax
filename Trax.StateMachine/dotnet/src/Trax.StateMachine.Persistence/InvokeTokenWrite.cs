using Trax.Effect.Enums;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>A write to a row's <c>invoke_token</c>: set it to <see cref="Value"/>, or clear it when that is null.</summary>
/// <param name="Value">The token to store, or null to clear it.</param>
internal readonly record struct InvokeTokenWrite(string? Value);
