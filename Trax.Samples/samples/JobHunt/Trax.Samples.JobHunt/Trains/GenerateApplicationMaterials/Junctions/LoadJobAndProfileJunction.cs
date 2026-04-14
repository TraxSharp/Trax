using Microsoft.EntityFrameworkCore;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;

namespace Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials.Junctions;

public class LoadJobAndProfileJunction(JobHuntDbContext db)
    : Junction<GenerateApplicationMaterialsInput, MaterialsContext>
{
    public override async Task<MaterialsContext> Run(GenerateApplicationMaterialsInput input)
    {
        var job =
            await db.Jobs.FirstOrDefaultAsync(j => j.Id == input.JobId)
            ?? throw new InvalidOperationException($"Job {input.JobId} not found.");

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == input.UserId);
        if (profile is null)
            throw new InvalidOperationException(
                $"Profile for user {input.UserId} not found. Create a profile first."
            );

        return new MaterialsContext
        {
            Input = input,
            JobTitle = job.Title,
            JobCompany = job.Company,
            JobDescription = job.RawDescription,
            SkillsJson = profile.SkillsJson,
            EducationJson = profile.EducationJson,
            WorkHistoryJson = profile.WorkHistoryJson,
        };
    }
}
