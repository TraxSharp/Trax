using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Data.Entities;

namespace Trax.Samples.JobHunt.Trains.FindContact.Junctions;

public class PersistContactJunction(JobHuntDbContext db, ILogger<PersistContactJunction> logger)
    : Junction<FindContactInput, FindContactOutput>
{
    public override async Task<FindContactOutput> Run(FindContactInput input)
    {
        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            JobId = input.JobId,
            Name = input.Name,
            Email = input.Email,
            Verified = false,
            Source = "Manual",
            CreatedAt = DateTime.UtcNow,
        };

        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Recorded contact {ContactId} for job {JobId}",
            contact.Id,
            input.JobId
        );

        return new FindContactOutput
        {
            ContactId = contact.Id,
            Name = contact.Name,
            Email = contact.Email,
            Source = contact.Source,
        };
    }
}
