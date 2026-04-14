using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Data.Entities;

namespace Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials.Junctions;

public class PersistArtifactsJunction(JobHuntDbContext db, ILogger<PersistArtifactsJunction> logger)
    : Junction<MaterialsContext, GenerateApplicationMaterialsOutput>
{
    public override async Task<GenerateApplicationMaterialsOutput> Run(MaterialsContext ctx)
    {
        var now = DateTime.UtcNow;

        var resume = new Artifact
        {
            Id = Guid.NewGuid(),
            JobId = ctx.Input.JobId,
            UserId = ctx.Input.UserId,
            Type = ArtifactType.Resume,
            Content = ctx.ResumeMarkdown ?? "",
            ModelUsed = ctx.ResumeModel ?? "unknown",
            GeneratedAt = now,
        };

        var coverLetter = new Artifact
        {
            Id = Guid.NewGuid(),
            JobId = ctx.Input.JobId,
            UserId = ctx.Input.UserId,
            Type = ArtifactType.CoverLetter,
            Content = ctx.CoverLetterMarkdown ?? "",
            ModelUsed = ctx.CoverLetterModel ?? "unknown",
            GeneratedAt = now,
        };

        db.Artifacts.AddRange(resume, coverLetter);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Persisted resume {ResumeId} and cover letter {CoverLetterId} for job {JobId}",
            resume.Id,
            coverLetter.Id,
            ctx.Input.JobId
        );

        return new GenerateApplicationMaterialsOutput
        {
            ResumeArtifactId = resume.Id,
            CoverLetterArtifactId = coverLetter.Id,
            ResumeMarkdown = resume.Content,
            CoverLetterMarkdown = coverLetter.Content,
        };
    }
}
