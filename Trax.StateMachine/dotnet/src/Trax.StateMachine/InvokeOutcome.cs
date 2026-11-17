using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>
/// One run's outcome, as the persistence layer hands it to <see cref="SnapshotMachine{TState,TTrigger}.ApplyOutcome"/>.
/// </summary>
internal abstract record InvokeOutcome
{
    public abstract InvokeOutcomeKind Kind { get; }

    /// <summary>The run succeeded; <see cref="Output"/> is its output serialized with <see cref="InvokeDefinition{TState}.SerializeOutput"/>.</summary>
    public sealed record Done(JsonNode? Output) : InvokeOutcome
    {
        public override InvokeOutcomeKind Kind => InvokeOutcomeKind.Done;
    }

    /// <summary>The run failed.</summary>
    public sealed record Failed : InvokeOutcome
    {
        public override InvokeOutcomeKind Kind => InvokeOutcomeKind.Failed;
    }

    /// <summary>The run was cancelled.</summary>
    public sealed record Cancelled : InvokeOutcome
    {
        public override InvokeOutcomeKind Kind => InvokeOutcomeKind.Cancelled;
    }

    private InvokeOutcome() { }
}
