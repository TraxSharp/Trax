using System.Text.Json;
using Trax.Core.Junction;

namespace Trax.Samples.JobHunt.Trains.UpdateProfile.Junctions;

public class ValidateProfileJsonJunction : Junction<UpdateProfileInput, UpdateProfileInput>
{
    public override Task<UpdateProfileInput> Run(UpdateProfileInput input)
    {
        if (string.IsNullOrWhiteSpace(input.UserId))
            throw new ArgumentException("UserId is required.");

        if (string.IsNullOrWhiteSpace(input.Json))
            throw new ArgumentException("Json payload is required.");

        try
        {
            using var doc = JsonDocument.Parse(input.Json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Profile JSON must be a JSON array.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON: {ex.Message}", ex);
        }

        return Task.FromResult(input);
    }
}
