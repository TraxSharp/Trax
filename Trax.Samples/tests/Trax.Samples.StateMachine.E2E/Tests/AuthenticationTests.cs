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

    private static IEnumerable<TestCaseData> EveryOperation()
    {
        var id = Guid.NewGuid();
        yield return new TestCaseData(
            $$"""mutation { machines { turnstile { start(requestId: "start-anonymous") { id } } } }"""
        ).SetArgDisplayNames("turnstile.start");
        yield return new TestCaseData(
            $$"""mutation { machines { turnstile { coin(id: "{{id}}", input: { coin: "quarter" }, requestId: "coin-anonymous") { id } } } }"""
        ).SetArgDisplayNames("turnstile.coin");
        yield return new TestCaseData(
            $$"""mutation { machines { turnstile { push(id: "{{id}}", requestId: "push-anonymous") { id } } } }"""
        ).SetArgDisplayNames("turnstile.push");
        yield return new TestCaseData(
            $$"""{ machines { turnstile { get(id: "{{id}}") { id } } } }"""
        ).SetArgDisplayNames("turnstile.get");
        yield return new TestCaseData(
            $$"""mutation { machines { checkout { pay(id: "{{id}}", requestId: "pay-anonymous") { id } } } }"""
        ).SetArgDisplayNames("checkout.pay");
        yield return new TestCaseData(
            $$"""mutation { machines { checkout { discard(id: "{{id}}", requestId: "discard-anonymous") { id } } } }"""
        ).SetArgDisplayNames("checkout.discard");
    }

    [TestCaseSource(nameof(EveryOperation))]
    public async Task Anonymous_IsRefused(string document)
    {
        var response = await Machines.RawAsync(Anonymous, document);

        response.IsRefused.Should().BeTrue(response.Raw);
    }

    [TestCaseSource(nameof(EveryOperation))]
    public async Task AWrongKey_IsRefused(string document)
    {
        var stranger = new Caller("stranger", "not-a-key-this-host-knows");

        var response = await Machines.RawAsync(stranger, document);

        response.IsRefused.Should().BeTrue(response.Raw);
    }

    [Test]
    public async Task Anonymous_WritesNothing()
    {
        var requestId = $"start-{Guid.NewGuid():N}";
        var response = await Machines.RawAsync(
            Anonymous,
            $$"""mutation { machines { turnstile { start(requestId: "{{requestId}}") { id } } } }"""
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
        var machines = new StateMachineClient(new GraphQLClient(factory.CreateClient()));

        var schemes = await factory
            .Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync();
        schemes.Select(s => s.Name).Should().NotContain(ApiKeyDefaults.SchemeName, Adr);

        var response = await machines.RawAsync(
            Alice,
            """mutation { machines { turnstile { start(requestId: "start-production") { id } } } }"""
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
