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
/// The four mutations carry <c>[TraxAuthorize]</c>, so an anonymous caller is refused with Trax's
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

    private static IEnumerable<TestCaseData> EveryMutation()
    {
        var id = Guid.NewGuid();
        yield return new TestCaseData(
            "saveSnapshot",
            "SaveSnapshotInput",
            new
            {
                machine = "turnstile",
                id,
                snapshot = """{"machine":"turnstile","version":1,"state":"Locked","context":{}}""",
            }
        ).SetArgDisplayNames("saveSnapshot");
        yield return new TestCaseData(
            "advanceSnapshot",
            "AdvanceSnapshotInput",
            new
            {
                machine = "turnstile",
                id,
                trigger = "Coin",
            }
        ).SetArgDisplayNames("advanceSnapshot");
        yield return new TestCaseData(
            "loadSnapshot",
            "LoadSnapshotInput",
            new { machine = "turnstile", id }
        ).SetArgDisplayNames("loadSnapshot");
        yield return new TestCaseData(
            "sendSnapshot",
            "SendSnapshotInput",
            new { machine = "checkout", id }
        ).SetArgDisplayNames("sendSnapshot");
    }

    [TestCaseSource(nameof(EveryMutation))]
    public async Task Anonymous_IsRefused(string field, string inputType, object input)
    {
        var response = await Machines.RawAsync(Anonymous, field, inputType, input);

        response.IsRefused.Should().BeTrue(response.Raw);
    }

    [TestCaseSource(nameof(EveryMutation))]
    public async Task AWrongKey_IsRefused(string field, string inputType, object input)
    {
        var stranger = new Caller("stranger", "not-a-key-this-host-knows");

        var response = await Machines.RawAsync(stranger, field, inputType, input);

        response.IsRefused.Should().BeTrue(response.Raw);
    }

    [Test]
    public async Task Anonymous_WritesNothing()
    {
        var id = Guid.NewGuid();
        var response = await Machines.RawAsync(
            Anonymous,
            "saveSnapshot",
            "SaveSnapshotInput",
            new
            {
                machine = Turnstile,
                id,
                snapshot = TurnstileWire("Locked"),
            }
        );
        response.IsRefused.Should().BeTrue(response.Raw);

        // Drafts are keyed by caller, so loading as anyone else could never see an anonymous
        // write. Read the table itself, across every caller.
        await using var connection = new NpgsqlConnection(
            TestPostgres.WithPort(StateMachineApiFactory.DefaultConnectionString)
        );
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand(
            "select count(*) from trax.snapshot_draft where id = @id",
            connection
        );
        count.Parameters.AddWithValue("id", id);

        ((long)(await count.ExecuteScalarAsync())!)
            .Should()
            .Be(0, "a refused save stores no draft under any caller");
    }

    [Test]
    public async Task Development_TheDemoKeyWorks()
    {
        var id = Guid.NewGuid();

        (await Machines.SaveAsync(Alice, Turnstile, id, TurnstileWire("Locked")))
            .State.Should()
            .Be("Locked");
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
            "saveSnapshot",
            "SaveSnapshotInput",
            new
            {
                machine = Turnstile,
                id = Guid.NewGuid(),
                snapshot = TurnstileWire("Locked"),
            }
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
