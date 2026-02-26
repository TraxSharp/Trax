using Trax.Core.Step;
using LanguageExt;
using Microsoft.Extensions.Logging;

namespace Trax.Samples.Scheduler.Workflows.AlwaysFails.Steps;

/// <summary>
/// A step that always throws an exception after logging what it's about to do.
/// This simulates a realistic failure with a meaningful error message and stack trace.
/// </summary>
public class ThrowExceptionStep(ILogger<ThrowExceptionStep> logger) : Step<AlwaysFailsInput, Unit>
{
    public override async Task<Unit> Run(AlwaysFailsInput input)
    {
        logger.LogWarning("Scenario '{Scenario}': about to simulate a failure...", input.Scenario);

        await Task.Delay(50);

        throw new InvalidOperationException(
            $"Simulated failure for scenario '{input.Scenario}'. "
                + "This workflow is intentionally designed to always fail so that it dead-letters "
                + "and can be used to test the dead letter detail page."
        );
    }
}
