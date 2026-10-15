using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Every machine is looked up by its id, so a host whose registered machines give one id to two types refuses to
/// start, naming both, rather than answer every machine request <c>INTERNAL_ERROR</c>. A subclass has its base's id,
/// so serving a machine by deriving a marked class from it means registering only the subclass. The same type
/// registered twice is one machine: the host starts and serves it once, and every other machine answers as before.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineRegistrationE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_registration";

    private IHost? _host;

    [SetUp]
    public Task SetUp() => E2EHost.RecreateDatabaseAsync(Database);

    [TearDown]
    public async Task TearDown()
    {
        await ShelfHosts.StopAsync(_host);
        _host = null;
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task A_base_machine_registered_beside_its_marked_subclass_refuses_the_host_naming_both()
    {
        var act = async () =>
            _host = await E2EHost.StartAsync(
                Database,
                new CountingCharge(),
                new E2EHostOptions
                {
                    Served = [typeof(ShelfMachine), typeof(WizardMachine)],
                    // The library's own registration of the machine the host serves through a marked subclass.
                    Services = s => s.AddSingleton(typeof(IMachine), typeof(ShelfMachine)),
                }
            );

        (
            await act.Should()
                .ThrowAsync<InvalidOperationException>(
                    "a subclass has its base's id, so the two would break every machine request, per "
                        + Adr
                )
        )
            .Which.Message.Should()
            .Contain($"'{ShelfMachine.MachineId}'")
            .And.Contain(typeof(ShelfMachine).FullName!)
            .And.Contain(typeof(ServedShelfMachine).FullName!)
            .And.Contain($"Register only {typeof(ServedShelfMachine).FullName}");
    }

    [Test]
    public async Task The_same_machine_registered_twice_starts_serves_it_once_and_every_machine_answers()
    {
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(ShelfMachine), typeof(WizardMachine)],
                Services = s => s.AddSingleton(typeof(IMachine), typeof(ServedShelfMachine)),
            }
        );

        var shelf = await _host.StartShelfAsync(E2EHost.AliceKey);
        using (var get = await _host.PostAsync(ShelfGet(shelf), apiKey: E2EHost.AliceKey))
            ReadShelf(get)
                .GetProperty("id")
                .GetString()
                .Should()
                .Be(shelf.ToString(), "the machine registered twice is served once, per " + Adr);

        var wizard = await _host.StartWizardAsync(E2EHost.AliceKey);
        (await _host.GetWizardAsync(E2EHost.AliceKey, wizard.Id))
            .GetProperty("id")
            .GetString()
            .Should()
            .Be(wizard.Id.ToString(), "an unrelated machine answers as it would on its own");
    }

    private static string ShelfGet(Guid id) =>
        $"{{ machines {{ shelf {{ get(id: {WizardOps.Literal(id.ToString())}) {{ id state }} }} }} }}";

    private static JsonElement ReadShelf(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("errors", out var errors))
            Assert.Fail($"machines.shelf.get failed: {errors}");
        return doc
            .RootElement.GetProperty("data")
            .GetProperty("machines")
            .GetProperty("shelf")
            .GetProperty("get");
    }
}
