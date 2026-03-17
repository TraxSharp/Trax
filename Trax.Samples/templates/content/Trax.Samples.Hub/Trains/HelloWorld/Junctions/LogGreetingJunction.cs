using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.Hub.Trains.HelloWorld.Junctions;

public class LogGreetingJunction(ILogger<LogGreetingJunction> logger)
    : Junction<HelloWorldInput, Unit>
{
    public override async Task<Unit> Run(HelloWorldInput input)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        logger.LogInformation(
            "Hello, {Name}! This train ran at {Timestamp}",
            input.Name,
            timestamp
        );

        await Task.Delay(100);

        logger.LogInformation("HelloWorld train completed successfully for {Name}", input.Name);

        return Unit.Default;
    }
}
