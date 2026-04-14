using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Providers.Llm;

namespace Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials.Junctions;

public class GenerateResumeJunction(
    ILlmProvider llm,
    IOptions<OllamaOptions> options,
    ILogger<GenerateResumeJunction> logger
) : Junction<MaterialsContext, MaterialsContext>
{
    public override async Task<MaterialsContext> Run(MaterialsContext ctx)
    {
        var model = options.Value.ResumeModel;

        var prompt =
            ctx.Input.ResumePromptOverride
            ?? $"""
                Generate a tailored resume in Markdown format for the following job.

                Job Title: {ctx.JobTitle}
                Company: {ctx.JobCompany}
                Job Description: {ctx.JobDescription}

                Candidate Skills: {ctx.SkillsJson}
                Candidate Education: {ctx.EducationJson}
                Candidate Work History: {ctx.WorkHistoryJson}

                Write a clean, professional resume that highlights relevant experience
                and skills for this specific role. Output only the Markdown content.
                """;

        logger.LogInformation("Generating resume with model {Model}", model);
        var markdown = await llm.GenerateAsync(prompt, model);

        return ctx with
        {
            ResumeMarkdown = markdown,
            ResumeModel = model,
        };
    }
}
