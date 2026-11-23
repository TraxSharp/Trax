using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>Where a <see cref="SetStep"/> takes its value from.</summary>
public abstract record ValueSource
{
    /// <summary>Copy a field out of the trigger input.</summary>
    public sealed record FromInput(string Field) : ValueSource;

    /// <summary>A literal JSON value.</summary>
    public sealed record Constant(JsonNode? Value) : ValueSource;

    private ValueSource() { }
}
