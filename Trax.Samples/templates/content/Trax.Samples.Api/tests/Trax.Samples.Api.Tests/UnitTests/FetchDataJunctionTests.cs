using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Trax.Samples.Api.Trains.Lookup;
using Trax.Samples.Api.Trains.Lookup.Junctions;

namespace Trax.Samples.Api.Tests.UnitTests;

/// <summary>
/// A junction is a class with a Run method, so it is tested by constructing it with its
/// dependencies and calling Run: no container, no database, no host.
/// </summary>
[TestFixture]
public class FetchDataJunctionTests
{
    [Test]
    public async Task Run_WithAnId_ReturnsTheRecordForThatId()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var junction = new FetchDataJunction(NullLogger<FetchDataJunction>.Instance, clock);

        var output = await junction.Run(new LookupInput { Id = "42" });

        Assert.That(output.Id, Is.EqualTo("42"), "the lookup returns the record it was asked for");
        Assert.That(
            output.CreatedAt,
            Is.EqualTo(clock.GetUtcNow().UtcDateTime),
            "the record is stamped with the clock the junction was given"
        );
    }
}
