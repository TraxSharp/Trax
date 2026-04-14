namespace Trax.Samples.JobHunt.Providers.Contact;

public record ContactResult(string? Name, string? Email, bool Verified, string Source);

public interface IContactEnrichmentProvider
{
    Task<ContactResult> EnrichAsync(
        string companyDomain,
        string? knownName,
        CancellationToken ct = default
    );
}
