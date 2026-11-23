using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>The turnstile, authored fluently and discovered by <c>AddTraxStateMachines</c> — no effect, no committed state.</summary>
/// <summary>What a turnstile or an order starts with: nothing.</summary>
public sealed record EmptyStart;
