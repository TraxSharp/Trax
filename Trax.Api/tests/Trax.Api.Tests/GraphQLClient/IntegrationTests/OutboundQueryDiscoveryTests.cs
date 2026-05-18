using FluentAssertions;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// OutboundQueryDiscovery is the bridge between annotated request types and the dashboard's
/// "which trains call which external endpoints" view. The contract: discovery must include
/// every tagged request, exclude untagged ones, and report the operation name parsed from
/// the query string so the dashboard has something to display besides the type's CLR name.
/// </summary>
[TestFixture]
public class OutboundQueryDiscoveryTests
{
    [Test]
    public void Discover_FindsAllTaggedRequests()
    {
        var assembly = typeof(TaggedPlayerQuery).Assembly;

        var entries = OutboundQueryDiscovery.Discover(assembly);

        entries
            .Select(e => e.RequestType)
            .Should()
            .Contain(typeof(TaggedPlayerQuery))
            .And.Contain(typeof(TaggedItemQuery))
            .And.Contain(typeof(TaggedBillingMutation));
    }

    [Test]
    public void Discover_ExcludesUntaggedRequests()
    {
        var assembly = typeof(UntaggedQuery).Assembly;

        var entries = OutboundQueryDiscovery.Discover(assembly);

        entries.Select(e => e.RequestType).Should().NotContain(typeof(UntaggedQuery));
    }

    [Test]
    public void Discover_GroupsByEndpointName()
    {
        var entries = OutboundQueryDiscovery.Discover(typeof(TaggedPlayerQuery).Assembly);

        var byEndpoint = entries.GroupBy(e => e.Endpoint).ToDictionary(g => g.Key, g => g.ToList());

        byEndpoint["PlayerService"].Should().HaveCount(2);
        byEndpoint["BillingService"].Should().HaveCount(1);
    }

    [Test]
    public void Discover_ParsesOperationNameFromQuery()
    {
        var entries = OutboundQueryDiscovery.Discover(typeof(TaggedPlayerQuery).Assembly);

        entries
            .Single(e => e.RequestType == typeof(TaggedPlayerQuery))
            .QueryName.Should()
            .Be("GetPlayerTagged");
        entries
            .Single(e => e.RequestType == typeof(TaggedBillingMutation))
            .QueryName.Should()
            .Be("BillUserTagged");
    }

    [Test]
    public void Discover_NullAssemblies_Throws()
    {
        var act = () => OutboundQueryDiscovery.Discover(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Attribute_NullOrWhitespaceEndpoint_Throws()
    {
        var actNull = () => new TraxOutboundQueryAttribute(null!);
        actNull.Should().Throw<ArgumentException>();
        var actEmpty = () => new TraxOutboundQueryAttribute("  ");
        actEmpty.Should().Throw<ArgumentException>();
    }
}
