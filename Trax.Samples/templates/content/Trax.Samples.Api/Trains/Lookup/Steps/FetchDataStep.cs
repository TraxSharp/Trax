using Trax.Core.Models;

namespace Trax.Samples.Api.Trains.Lookup.Steps;

public class FetchDataStep(ILogger<FetchDataStep> logger) : Step<LookupInput, LookupOutput>
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
