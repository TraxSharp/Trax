using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.FindContact;
using Trax.Samples.JobHunt.Trains.FindContact.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.FindContact;

[TestFixture]
public class PersistContactJunctionTests
{
    [Test]
    public async Task Run_PersistsContact()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistContactJunction(db, NullLogger<PersistContactJunction>.Instance);

        var result = await junction.Run(
            new FindContactInput
            {
                JobId = Guid.NewGuid(),
                Name = "Jane Recruiter",
                Email = "jane@acme.com",
            }
        );

        result.ContactId.Should().NotBeEmpty();
        result.Name.Should().Be("Jane Recruiter");
        result.Email.Should().Be("jane@acme.com");
        result.Source.Should().Be("Manual");

        var contact = await db.Contacts.SingleAsync();
        contact.Name.Should().Be("Jane Recruiter");
    }
}
