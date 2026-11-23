namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

/// <summary>
/// Input model for nested train tests.
/// Used to test hierarchical train memory patterns.
/// </summary>
public record NestedTestInput
{
    /// <summary>
    /// Unique identifier for the parent train.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Collection of child train inputs.
    /// </summary>
    public required IList<MemoryTestInput> ChildInputs { get; init; }

    /// <summary>
    /// Description of the nested test scenario.
    /// </summary>
    public string? Description { get; init; }
}
