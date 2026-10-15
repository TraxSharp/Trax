using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Api.Auth.ApiKey;
using Trax.Samples.StateMachine.E2E.Factories;
using Trax.Samples.StateMachine.E2E.Fixtures;
using Trax.Samples.StateMachine.E2E.Utilities;

namespace Trax.Samples.StateMachine.E2E.Tests;

/// <summary>
/// Every typed machine operation requires an authenticated caller, so an anonymous one is refused with Trax's
/// opaque authorization error before any draft is read or written; <c>listMachines</c> is anonymous.
/// The demo keys are published in this repository, so they exist only in Development: started in
/// Production, the host registers no key and refuses them like any other anonymous caller.
///
/// <para>Enforces <c>Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md</c> for this
/// sample.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md")]
public class AuthenticationTests : StateMachineTestFixture
{
    private const string Adr =
        "see Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md";

    // Each operation as the page sends it: its generated id and its variables.
    private static IEnumerable<TestCaseData> EveryOperation()
    {
        var id = Guid.NewGuid();
        yield return Case("turnstile", "start", new { requestId = "start-anonymous" });
        yield return Case(
            "turnstile",
            "coin",
            new
            {
                id,
                input = new { coin = "quarter" },
                requestId = "coin-anonymous",
            }
        );
        yield return Case("turnstile", "push", new { id, requestId = "push-anonymous" });
        yield return Case("turnstile", "get", new { id });
        yield return Case("checkout", "pay", new { id, requestId = "pay-anonymous" });
        yield return Case("checkout", "discard", new { id, requestId = "discard-anonymous" });
    }

    private static TestCaseData Case(string machine, string operation, object variables) =>
        new TestCaseData(
            StateMachineClient.DocumentOf(machine, operation)!.Id,
            variables
        ).SetArgDisplayNames($"{machine}.{operation}");

    [TestCaseSource(nameof(EveryOperation))]
    public async Task Anonymous_IsRefused(string id, object variables)
    {
        var response = await GraphQL.SendIdAsync(id, Anonymous, variables);

        response.IsRefused.Should().BeTrue(response.Raw);
    }

    [TestCaseSource(nameof(EveryOperation))]
    public async Task AWrongKey_IsRefused(string id, object variables)
    {
        var stranger = new Caller("stranger", "not-a-key-this-host-knows");

        var response = await GraphQL.SendIdAsync(id, stranger, variables);

        response.IsRefused.Should().BeTrue(response.Raw);
    }

    [Test]
    public async Task Anonymous_WritesNothing()
    {
        var requestId = $"start-{Guid.NewGuid():N}";
        var response = await GraphQL.SendIdAsync(
            StateMachineClient.DocumentOf(Turnstile, "start")!.Id,
            Anonymous,
            new { requestId }
        );
        response.IsRefused.Should().BeTrue(response.Raw);

        // Drafts are keyed by caller, so loading as anyone else could never see an anonymous
        // write. Read the table itself, across every caller.
        await using var connection = new NpgsqlConnection(
            TestPostgres.WithPort(StateMachineApiFactory.DefaultConnectionString)
        );
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand(
            "select count(*) from trax.snapshot_draft where start_request_id = @requestId",
            connection
        );
        count.Parameters.AddWithValue("requestId", requestId);

        ((long)(await count.ExecuteScalarAsync())!)
            .Should()
            .Be(0, "a refused start stores no draft under any caller");
    }

    [Test]
    public async Task Development_TheDemoKeyWorks()
    {
        var (_, started) = await Machines.StartAsync(Alice, Turnstile);

        started.State.Should().Be("Locked");
    }

    [Test]
    public async Task Production_RegistersNoApiKey_AndRefusesTheDemoKey()
    {
        await using var factory = new ProductionStateMachineApiFactory();
        var graphQL = new GraphQLClient(factory.CreateClient());
        var machines = new StateMachineClient(graphQL);

        var schemes = await factory
            .Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync();
        schemes.Select(s => s.Name).Should().NotContain(ApiKeyDefaults.SchemeName, Adr);

        var response = await graphQL.SendIdAsync(
            StateMachineClient.DocumentOf(Turnstile, "start")!.Id,
            Alice,
            new { requestId = "start-production" }
        );
        response.IsRefused.Should().BeTrue($"{Adr}: {response.Raw}");

        var list = await machines.ListMachinesAsync(Anonymous);
        list.HasErrors.Should().BeFalse("listMachines is anonymous in every environment");
    }

    [Test]
    public async Task Production_RefusesToStart_WithADemoKeyRegistered()
    {
        // What happens when someone copies the registration out of its IsDevelopment block: Trax.Api
        // refuses to start the host, naming the marker.
        await using var factory = new ProductionStateMachineApiFactory(builder =>
            builder.ConfigureTestServices(services =>
                services.AddTraxApiKeyAuth(keys =>
                    keys.Add(Caller.Alice.ApiKey!, id: "alice", "User")
                )
            )
        );

        var start = () => factory.Services;

        start
            .Should()
            .Throw<Exception>()
            .Where(e => e.ToString().Contains("do-not-use-in-production"), Adr);
    }
}
