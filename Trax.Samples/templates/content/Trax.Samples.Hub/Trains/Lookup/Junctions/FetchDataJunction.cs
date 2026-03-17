using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.Hub.Trains.Lookup.Junctions;

public class FetchDataJunction(ILogger<FetchDataJunction> logger)
    : Junction<LookupInput, LookupOutput>
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
