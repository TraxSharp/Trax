using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Extensions;
using Trax.Core.Functional;

namespace Trax.Core.Monad;

/// <summary>
/// One thing wrong with a declared chain.
/// </summary>
/// <param name="StepIndex">Position of the offending step, counting from zero.</param>
/// <param name="Kind">Which chain primitive declared the step.</param>
/// <param name="Junction">The junction the step names, or null for a step that names none.</param>
/// <param name="Reason">What is wrong, phrased for whoever has to fix it.</param>
/// <remarks>
/// A refusal about the chain as a whole, rather than one step, has a <see cref="StepIndex"/> one
/// past the last step, <see cref="ChainStepKind.Resolve"/> as its kind and no junction.
/// </remarks>
public readonly record struct ChainFault(
    int StepIndex,
    ChainStepKind Kind,
    Type? Junction,
    string Reason
)
{
    /// <summary>
    /// True when the fault is one of <see cref="ChainRecorder.Refusals"/>: something reading the
    /// chain refused, rather than a type the replay found missing. A refused step records no
    /// output, so later faults may be its consequences rather than faults of their own.
    /// </summary>
    public bool IsRefusal { get; init; }
}
