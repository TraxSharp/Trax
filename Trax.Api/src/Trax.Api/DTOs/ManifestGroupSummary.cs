namespace Trax.Api.DTOs;

public record ManifestGroupSummary(
    long Id,
    string Name,
    int? MaxActiveJobs,
    int Priority,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
