using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Gherkin.Reqnroll;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The same scenarios, run on the derived machines in memory and against a host serving them, answer the same (ADR
/// <c>Trax.Docs/adr/0058</c>): the same refusal codes and messages, the same states, the same views. The proof of
/// concept's in-memory runner refused with words the host does not send, and its host bindings passed a denial on a
/// <c>NOT_FOUND</c>.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ScenarioDriverParityTests
{
    private const string Database = "trax_sm_scenario_parity";

    private const string Glossary = """
        {
          "version": 1,
          "actors": { "payer": { "noun": "a payer", "roles": ["Payer"] } },
          "machines": {
            "p-order": {
              "ownedBy": "payer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "context": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
              "states": { "Draft": "is a draft", "Placed": "is placed" },
              "triggers": {
                "Order": {
                  "phrase": "orders {loaves} loaves for {subject}",
                  "input": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "loaves", "value": { "input": "loaves" } } ] }
                },
                "Place": { "phrase": "places {subject}" },
                "Rush": { "phrase": "rushes {subject}" }
              },
              "guards": {
                "hasLoaves": {
                  "holds": "has something in it",
                  "fails": "has nothing in it",
                  "rule": { "rule": "compare", "source": "context", "field": "loaves", "op": "gt", "value": 0 }
                }
              }
            }
          },
          "views": {
            "orders": {
              "reads": [ { "machine": "p-order" } ],
              "fields": { "status": { "from": "p-order.state", "labels": { "Draft": "Not sent", "Placed": "Sent" } } }
            }
          }
        }
        """;

    private const string Ordering = """
        Feature: Ordering
          Background:
            Given Ada is a payer
            And Ada has an order

          Scenario: Ordering then placing
            When Ada orders "3" loaves for her order
            Then Ada's order is a draft
            When Ada places her order
            Then Ada's order is placed
            And Ada's orders show status "Sent"

          Scenario: Placing an empty order
            Given Ada's order has nothing in it
            When Ada places her order
            Then Ada is told "Add something first."
            And Ada's order is still a draft

          Scenario: Placing what has something in it
            Given Ada's order has something in it
            When Ada places her order
            Then Ada's order is placed

          Scenario: Nobody rushes an order
            When Ada rushes her order
            Then Ada is not allowed
        """;

    private IHost _host = null!;
    private Glossary _glossary = null!;
    private Dictionary<string, JsonObject> _irs = null!;
    private HostFile _hostFile = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        _glossary = Trax.StateMachine.Features.Glossary.Read(Glossary);
        var derived = FeatureDerivation.Derive(_glossary, [("ordering.feature", Ordering)]);
        derived.Problems.Should().BeEmpty();
        _irs = derived.Irs.ToDictionary(kv => kv.Key, kv => JsonNode.Parse(kv.Value)!.AsObject());
        _hostFile = HostFile.Read(derived.Host!);
        var machines = IrMachines.Create(
            _irs.Select(kv => new IrMachines.Source(kv.Value, _hostFile.Machines[kv.Key])).ToList(),
            []
        );

        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Instances = machines,
                Views = new MachineViewCatalog(_hostFile.Views),
                Services = s => s.AddSingleton(_hostFile),
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task Every_scenario_answers_the_same_in_memory_and_on_the_host()
    {
        var (scenarios, problems) = FeatureFiles.Parse([("ordering.feature", Ordering)]);
        problems.Should().BeEmpty();
        var resolver = new StepResolver(_glossary);
        foreach (var scenario in scenarios)
        {
            var walk = Walk.Run(scenario, _glossary, resolver);
            var memory = await ScenarioRunner.Run(
                walk,
                _glossary,
                new MemoryDriver(_irs, _hostFile, _glossary)
            );
            var host = await ScenarioRunner.Run(walk, _glossary, HostDriver(scenario.Name));

            memory.Status.Should().Be(ScenarioStatus.Passed, Show(memory));
            host.Status.Should().Be(ScenarioStatus.Passed, Show(host));
        }
    }

    [Test]
    public async Task A_guard_refuses_with_the_same_code_and_message_on_both()
    {
        var ada = new Person("Ada", _glossary.Actors["payer"]);
        var key = new InstanceKey("p-order", "Ada");
        IScenarioDriver[] drivers =
        [
            new MemoryDriver(_irs, _hostFile, _glossary),
            HostDriver("guard"),
        ];
        var answers = new List<Answer>();
        foreach (var driver in drivers)
        {
            await driver.Start(ada, "p-order");
            answers.Add(await driver.Fire(ada, key, "Place", null));
        }

        answers[0]
            .Should()
            .Be(
                new Answer.Refused(
                    Trax.StateMachine.RefusalCode.GuardFailed,
                    "Add something first."
                )
            );
        answers[1].Should().Be(answers[0]);
    }

    [Test]
    public async Task A_trigger_no_one_is_served_is_not_served_on_the_host_either()
    {
        var ada = new Person("Ada", _glossary.Actors["payer"]);
        var driver = HostDriver("rush");
        await driver.Start(ada, "p-order");

        (await driver.Fire(ada, new InstanceKey("p-order", "Ada"), "Rush", null))
            .Should()
            .BeOfType<Answer.NotServed>();
    }

    private HostDriver HostDriver(string scenario) =>
        new(
            new TraxFeatureDriverOptions
            {
                Host = _host.Services,
                Client = _host.GetTestServer().CreateClient(),
                Glossary = FeatureGlossary.Read(Glossary),
                Credential = (_, _, _) =>
                    new FeatureCredential(
                        new Dictionary<string, string> { ["X-Api-Key"] = E2EHost.AliceKey },
                        "TraxApiKey:alice"
                    ),
            },
            _glossary,
            scenario
        );

    private static string Show(ScenarioResult result) =>
        $"{result.Scenario.Name}: "
        + string.Join("; ", result.Steps.Select(s => $"{s.Step.Text} {s.Status} {s.Message}"));
}
