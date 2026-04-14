using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Providers.Llm;

namespace Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials.Junctions;

public class GenerateCoverLetterJunction(
    ILlmProvider llm,
    IOptions<OllamaOptions> options,
    ILogger<GenerateCoverLetterJunction> logger
) : Junction<MaterialsContext, MaterialsContext>
{
    public override async Task<MaterialsContext> Run(MaterialsContext ctx)
    {
        var model = options.Value.CoverLetterModel;

        var prompt =
            ctx.Input.CoverLetterPromptOverride
            ?? $"""
                Write a cover letter in Markdown format for the following job application.

                Job Title: {ctx.JobTitle}
                Company: {ctx.JobCompany}
                Job Description: {ctx.JobDescription}

                Candidate Skills: {ctx.SkillsJson}
                Candidate Education: {ctx.EducationJson}
                Candidate Work History: {ctx.WorkHistoryJson}

                Write a concise, professional cover letter that connects the candidate's
                background to the role. Output only the Markdown content.
                """;

        logger.LogInformation("Generating cover letter with model {Model}", model);
        var markdown = await llm.GenerateAsync(prompt, model);

        return ctx with
        {
            CoverLetterMarkdown = markdown,
            CoverLetterModel = model,
        };
    }
}
