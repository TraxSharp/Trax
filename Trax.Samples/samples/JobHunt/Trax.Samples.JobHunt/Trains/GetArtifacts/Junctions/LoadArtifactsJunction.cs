using Microsoft.EntityFrameworkCore;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;

namespace Trax.Samples.JobHunt.Trains.GetArtifacts.Junctions;

public class LoadArtifactsJunction(JobHuntDbContext db)
    : Junction<GetArtifactsInput, GetArtifactsOutput>
{
    public override async Task<GetArtifactsOutput> Run(GetArtifactsInput input)
    {
        var artifacts = await db
            .Artifacts.Where(a => a.JobId == input.JobId && a.UserId == input.UserId)
            .OrderByDescending(a => a.GeneratedAt)
            .Select(a => new ArtifactSummary
            {
                Id = a.Id,
                Type = a.Type.ToString(),
                Content = a.Content,
                ModelUsed = a.ModelUsed,
                GeneratedAt = a.GeneratedAt,
            })
            .ToListAsync();

        return new GetArtifactsOutput { Artifacts = artifacts };
    }
}
