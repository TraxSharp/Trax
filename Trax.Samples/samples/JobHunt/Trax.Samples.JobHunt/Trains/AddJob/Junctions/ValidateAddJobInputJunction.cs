using Trax.Core.Junction;

namespace Trax.Samples.JobHunt.Trains.AddJob.Junctions;

public class ValidateAddJobInputJunction : Junction<AddJobInput, AddJobInput>
{
    public override Task<AddJobInput> Run(AddJobInput input)
    {
        if (string.IsNullOrWhiteSpace(input.UserId))
            throw new ArgumentException("UserId is required.");

        var hasUrl = !string.IsNullOrWhiteSpace(input.Url);
        var hasPastedTitle = !string.IsNullOrWhiteSpace(input.PastedTitle);
        var hasPastedCompany = !string.IsNullOrWhiteSpace(input.PastedCompany);
        var hasPastedDescription = !string.IsNullOrWhiteSpace(input.PastedDescription);
        var hasAnyPasted = hasPastedTitle || hasPastedCompany || hasPastedDescription;
        var hasAllPasted = hasPastedTitle && hasPastedCompany && hasPastedDescription;

        if (hasUrl && hasAnyPasted)
            throw new ArgumentException("Provide either a URL or pasted fields, not both.");

        if (!hasUrl && !hasAllPasted)
            throw new ArgumentException(
                "Provide either a URL or all three pasted fields (title, company, description)."
            );

        return Task.FromResult(input);
    }
}
