using Trax.Core.Step;
using LanguageExt;
using Microsoft.Extensions.Logging;

namespace Trax.Samples.Server.Workflows.HelloWorld.Steps;

/// <summary>
/// A step that logs a greeting message.
/// Demonstrates how steps can use dependency injection for logging and other services.
/// </summary>
public class LogGreetingStep(ILogger<LogGreetingStep> logger) : Step<HelloWorldInput, Unit>
{
    public override async Task<Unit> Run(HelloWorldInput input)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        logger.LogInformation(
            "Hello, {Name}! This scheduled job ran at {Timestamp}",
            input.Name,
            timestamp
        );

        await Task.Delay(100);

        logger.LogInformation("HelloWorld workflow completed successfully for {Name}", input.Name);

        return Unit.Default;
    }
}
