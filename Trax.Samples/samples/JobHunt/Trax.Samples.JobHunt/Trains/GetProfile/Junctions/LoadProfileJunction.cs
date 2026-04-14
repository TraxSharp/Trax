using Microsoft.EntityFrameworkCore;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;

namespace Trax.Samples.JobHunt.Trains.GetProfile.Junctions;

public class LoadProfileJunction(JobHuntDbContext db) : Junction<GetProfileInput, GetProfileOutput>
{
    public override async Task<GetProfileOutput> Run(GetProfileInput input)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == input.UserId);

        return new GetProfileOutput
        {
            UserId = input.UserId,
            SkillsJson = profile?.SkillsJson ?? "[]",
            EducationJson = profile?.EducationJson ?? "[]",
            WorkHistoryJson = profile?.WorkHistoryJson ?? "[]",
        };
    }
}
