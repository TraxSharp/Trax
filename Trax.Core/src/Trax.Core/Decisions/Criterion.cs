namespace Trax.Core.Decisions;

/// <summary>
/// One option or level: the name an answer uses, and what it means.
/// </summary>
public sealed record Criterion(string Name, string? Description);
