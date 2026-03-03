using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.Api.Rest.Trains.Greet.Steps;

public class LogGreetingStep(ILogger<LogGreetingStep> logger) : Step<GreetInput, Unit>
{
    public override async Task<Unit> Run(GreetInput input)
    {
        logger.LogInformation(
            "Hello, {Name}! (via REST API at {Time})",
            input.Name,
            DateTime.UtcNow
        );
        await Task.Delay(100);
        return Unit.Default;
    }
}
