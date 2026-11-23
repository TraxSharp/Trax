using System.Globalization;

namespace Trax.Cli.Machines;

/// <summary>What to generate, and where. Each artifact has its own output root because a real consumer splits
/// them across trees (the IR and corpus in a shared machines dir, the twin next to the frontend).
/// <see cref="ImportStyle"/> left null is <c>relative</c> with <see cref="EngineSrc"/> and <c>specifier</c>
/// without it.</summary>
internal sealed record MachineGenerateOptions(
    MachineIr Machine,
    string? IrOut = null,
    string? TwinOut = null,
    string? CorpusOut = null,
    string? EngineSrc = null,
    string? ImportStyle = null,
    string? Specifier = null,
    string? ToolsDir = null,
    string? Lang = null,
    string? Namespace = null
);
