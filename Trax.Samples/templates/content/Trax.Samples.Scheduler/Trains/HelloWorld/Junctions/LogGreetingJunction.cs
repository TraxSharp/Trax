using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Scheduler.Trains.HelloWorld.Junctions;

/// <summary>
/// EffectJunction rather than Junction: junction effect providers such as AddJunctionProgress
/// (the running junction, cancellation between junctions) only see EffectJunction. Plain
/// Junction&lt;TIn, TOut&gt; works when no provider needs to.
/// </summary>
public class LogGreetingJunction(ILogger<LogGreetingJunction> logger)
    : EffectJunction<HelloWorldInput, Unit>
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

        logger.LogInformation("HelloWorld train completed successfully for {Name}", input.Name);

        return Unit.Default;
    }
}
