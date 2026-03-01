using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.Scheduler.Workflows.GoodbyeWorld.Steps;

/// <summary>
/// A step that logs a farewell message.
/// </summary>
public class LogFarewellStep(ILogger<LogFarewellStep> logger) : Step<GoodbyeWorldInput, Unit>
{
    public override async Task<Unit> Run(GoodbyeWorldInput input)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        logger.LogInformation(
            "Goodbye, {Name}! This dependent job ran at {Timestamp}",
            input.Name,
            timestamp
        );

        await Task.Delay(100);

        logger.LogInformation(
            "GoodbyeWorld workflow completed successfully for {Name}",
            input.Name
        );

        return Unit.Default;
    }
}
