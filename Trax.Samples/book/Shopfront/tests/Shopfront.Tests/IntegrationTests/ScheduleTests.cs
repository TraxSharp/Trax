using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;
using Trax.Core.Extensions;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class ScheduleTests
{
    private WebApplicationFactory<Program>? _app;

    [SetUp]
    public async Task StartTheApplication()
    {
        await TestDatabase.Reset();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:Shopfront", TestDatabase.ConnectionString)
        );
    }

    [TearDown]
    public async ValueTask StopTheApplication()
    {
        if (_app is not null)
            await _app.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task Manifests_TheReportAndTheCheckAreScheduled()
    {
        var client = _app.NotNull().CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint("lin", Roles.Staff, TimeProvider.System)
        );

        var response = await client.Post(
            "{ operations { manifests(take: 10) { items { externalId cronExpression } } } }"
        );

        var manifests = response
            .GetProperty("data")
            .GetProperty("operations")
            .GetProperty("manifests")
            .GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                m => m.GetProperty("externalId").GetString().NotNull(),
                m => m.GetProperty("cronExpression").GetString(),
                StringComparer.Ordinal
            );
        manifests
            .Should()
            .ContainKey(ManifestNames.StockReport)
            .WhoseValue.Should()
            .Be("0 2 * * *");
        manifests.Should().ContainKey(ManifestNames.LowStockCheck);
    }
}
