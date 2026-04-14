using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Data.Entities;

namespace Trax.Samples.JobHunt.Trains.UpdateProfile.Junctions;

public class PersistProfileJunction(JobHuntDbContext db, ILogger<PersistProfileJunction> logger)
    : Junction<UpdateProfileInput, UpdateProfileOutput>
{
    public override async Task<UpdateProfileOutput> Run(UpdateProfileInput input)
    {
        var now = DateTime.UtcNow;

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == input.UserId);

        if (profile is null)
        {
            profile = new Profile
            {
                Id = Guid.NewGuid(),
                UserId = input.UserId,
                UpdatedAt = now,
            };
            db.Profiles.Add(profile);
        }

        switch (input.Facet)
        {
            case ProfileFacet.Skills:
                profile.SkillsJson = input.Json;
                break;
            case ProfileFacet.Education:
                profile.EducationJson = input.Json;
                break;
            case ProfileFacet.WorkHistory:
                profile.WorkHistoryJson = input.Json;
                break;
        }

        profile.UpdatedAt = now;
        await db.SaveChangesAsync();

        logger.LogInformation("Updated {Facet} for user {UserId}", input.Facet, input.UserId);

        return new UpdateProfileOutput
        {
            UserId = input.UserId,
            Facet = input.Facet,
            UpdatedAt = now,
        };
    }
}
