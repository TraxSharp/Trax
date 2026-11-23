using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>One field assignment in a <see cref="Reduction.Set"/>.</summary>
public sealed record SetStep(string Field, ValueSource Source);
