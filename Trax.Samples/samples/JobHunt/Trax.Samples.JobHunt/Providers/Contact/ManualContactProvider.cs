namespace Trax.Samples.JobHunt.Providers.Contact;

/// <summary>
/// Default provider: requires the user to supply name and email manually.
/// No enrichment is performed. This is the v1 baseline.
/// </summary>
public class ManualContactProvider : IContactEnrichmentProvider
{
    public Task<ContactResult> EnrichAsync(
        string companyDomain,
        string? knownName,
        CancellationToken ct = default
    )
    {
        return Task.FromResult(
            new ContactResult(Name: knownName, Email: null, Verified: false, Source: "Manual")
        );
    }
}
