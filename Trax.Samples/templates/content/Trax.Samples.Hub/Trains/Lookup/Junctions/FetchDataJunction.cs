using Microsoft.Extensions.Logging;
using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Hub.Trains.Lookup.Junctions;

/// <summary>
/// EffectJunction rather than Junction: junction effect providers such as AddJunctionProgress
/// (the running junction, cancellation between junctions) only see EffectJunction. Plain
/// Junction&lt;TIn, TOut&gt; works when no provider needs to.
/// </summary>
public class FetchDataJunction(ILogger<FetchDataJunction> logger)
    : EffectJunction<LookupInput, LookupOutput>
{
    public override async Task<LookupOutput> Run(LookupInput input)
    {
        logger.LogInformation("Looking up record {Id}", input.Id);

        // Replace this with your actual data access logic.
        await Task.Delay(50);

        return new LookupOutput
        {
            Id = input.Id,
            Name = $"Record {input.Id}",
            CreatedAt = DateTime.UtcNow,
        };
    }
}
