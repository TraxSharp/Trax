using Microsoft.AspNetCore.Hosting;
using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.E2E.Factories;
using Trax.Samples.Bookworm.E2E.Utilities;

namespace Trax.Samples.Bookworm.E2E.ApiTests;

/// <summary>
/// The same host started in Production: the demo keys are not registered, so no credential exists
/// and every member, loan and lending mutation is refused, while the anonymous catalog stays open.
/// </summary>
[TestFixture]
public class ProductionPostureTests
{
    private sealed class ProductionFactory : BookwormApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }

    private ProductionFactory _production = null!;
    private GraphQLClient _graphQL = null!;

    [OneTimeSetUp]
    public void StartProduction()
    {
        _production = new ProductionFactory();
        _graphQL = new GraphQLClient(_production.CreateClient());
    }

    [OneTimeTearDown]
    public async Task StopProduction() => await _production.DisposeAsync();

    [Test]
    public async Task The_demo_member_key_reads_no_member_in_Production()
    {
        var doc = await _graphQL.PostAsync(
            "{ discover { lending { members { nodes { email } } } } }",
            ApiKeyDefaults.MemberKey
        );

        doc.RootElement.GetRawText().Should().NotContain("@example.com");
        doc.RootElement.GetProperty("errors")[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("Not authorized.");
    }

    [Test]
    public async Task The_catalog_stays_public_in_Production()
    {
        var doc = await _graphQL.PostAsync(
            "{ discover { catalog { books { nodes { title } } } } }"
        );

        GraphQLClient.HasErrors(doc).Should().BeFalse(doc.RootElement.GetRawText());
    }
}
