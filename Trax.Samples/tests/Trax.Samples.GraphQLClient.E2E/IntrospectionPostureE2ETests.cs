using Microsoft.AspNetCore.Hosting;
using Trax.Samples.GraphQLClient.E2E.Factories;

namespace Trax.Samples.GraphQLClient.E2E;

/// <summary>
/// The two Trax servers serve their schema the way Trax does by default: to anyone in
/// Development, where the keyed clients read it, and to no one in Production.
/// </summary>
[TestFixture]
public class IntrospectionPostureE2ETests
{
    private const string IntrospectionQuery = """{"query":"{ __schema { queryType { name } } }"}""";

    [Test]
    public async Task Inventory_server_serves_its_schema_in_Development()
    {
        await using var factory = new InventoryServerFactory();
        var body = await Introspect(factory);

        body.Should().Contain("\"queryType\"").And.NotContain("\"errors\"");
    }

    [Test]
    public async Task Inventory_server_refuses_introspection_in_Production()
    {
        await using var factory = new InventoryServerFactory().WithWebHostBuilder(b =>
            b.UseEnvironment("Production")
        );
        var body = await Introspect(factory);

        body.Should().Contain("\"errors\"").And.NotContain("\"queryType\"");
    }

    [Test]
    public async Task Billing_server_serves_its_schema_in_Development()
    {
        await using var factory = new BillingServerFactory();
        var body = await Introspect(factory);

        body.Should().Contain("\"queryType\"").And.NotContain("\"errors\"");
    }

    [Test]
    public async Task Billing_server_refuses_introspection_in_Production()
    {
        await using var factory = new BillingServerFactory().WithWebHostBuilder(b =>
            b.UseEnvironment("Production")
        );
        var body = await Introspect(factory);

        body.Should().Contain("\"errors\"").And.NotContain("\"queryType\"");
    }

    private static async Task<string> Introspect<T>(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<T> factory
    )
        where T : class
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(
            IntrospectionQuery,
            System.Text.Encoding.UTF8,
            "application/json"
        );
        using var response = await client.PostAsync("/trax/graphql", content);
        return await response.Content.ReadAsStringAsync();
    }
}
