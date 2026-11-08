using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.StateMachine;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Gherkin.Reqnroll;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The same scenarios, run on the derived machines in memory and against a host serving them, answer the same (ADR
/// <c>Trax.Docs/adr/0058</c>): the same refusal codes and messages, the same states, the same views, the same policies
/// and timers. Every person signs in as a user of their own, so another owner's instance, a start or a view the rule
/// does not admit are answered by the host as it answers its clients. The in-memory runner never refuses with words the
/// host does not send, and the host bindings never pass a denial on a <c>NOT_FOUND</c>.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ScenarioDriverParityTests
{
    private const string Database = "trax_sm_scenario_parity";

    private const string Glossary = """
        {
          "version": 1,
          "actors": {
            "payer": { "noun": "a payer", "roles": ["Payer"] },
            "baker": { "noun": "a baker", "roles": ["Operator"] }
          },
          "machines": {
            "p-order": {
              "ownedBy": "payer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "context": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
              "states": { "Draft": "is a draft", "Placed": "is placed", "Baking": "is baking" },
              "triggers": {
                "Order": {
                  "phrase": "orders {loaves} loaves for {subject}",
                  "input": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "loaves", "value": { "input": "loaves" } } ] }
                },
                "Place": { "phrase": "places {subject}" },
                "Rush": { "phrase": "rushes {subject}" },
                "Bake": { "phrase": "{subject} starts baking" }
              },
              "guards": {
                "hasLoaves": {
                  "holds": "has something in it",
                  "fails": "has nothing in it",
                  "rule": { "rule": "compare", "source": "context", "field": "loaves", "op": "gt", "value": 0 }
                }
              }
            },
            "p-shift": {
              "ownedBy": "baker",
              "noun": "shift",
              "start": "has a shift",
              "initial": "Off",
              "states": { "Off": "is not started", "On": "is on" },
              "triggers": { "ClockIn": { "phrase": "clocks in for {subject}" } }
            },
            "p-shop": {
              "system": "p-shop/main",
              "name": "the shop",
              "initial": "Closed",
              "states": { "Closed": "is closed", "Open": "is open" },
              "triggers": { "Open": { "phrase": "the shop opens" } }
            }
          },
          "views": {
            "orders": {
              "reads": [ { "machine": "p-order" } ],
              "fields": { "status": { "from": "p-order.state", "labels": { "Draft": "Not sent", "Placed": "Sent", "Baking": "In the oven" } } }
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

    // A shift going on opens the system shop (a policy to a system machine), the shop opening starts every order
    // waiting for it (a broadcast from a system machine), and the shop closes on a timer.
    private const string Shop = """
        Feature: The shop
          Scenario: A baker clocking in opens the shop
            Given Kit is a baker
            And Kit has a shift
            And the shop is closed
            When Kit clocks in for his shift
            Then Kit's shift is on
            And the shop is open

          Scenario: Waiting orders start baking when the shop opens
            Given Ada is a payer
            And Ada has an order
            And Ada's order is placed
            And Kit is a baker
            And Kit has a shift
            And the shop is closed
            When Kit clocks in for his shift
            Then Kit's shift is on
            And the shop is open
            And Ada's order is baking
            And Ada's orders show status "In the oven"

          Scenario: The shop opens
            Given the shop is closed
            When the shop opens
            Then the shop is open

          Scenario: An order goes in the oven
            Given Ada is a payer
            And Ada has an order
            And Ada's order is placed
            When Ada's order starts baking
            Then Ada's order is baking

          Scenario: The shop is still open before closing time
            Given the shop is open
            When 2 seconds pass
            Then the shop is still open

          Scenario: The shop closes after hours
            Given the shop is open
            When 4 seconds pass
            Then the shop is closed
        """;

    private static readonly (string File, string Text)[] Features =
    [
        ("ordering.feature", Ordering),
        ("shop.feature", Shop),
    ];

    // Each person signs in as a user of their own, with their actor's roles, so an owner's instance is theirs alone.
    private static readonly Dictionary<string, (string Key, string Id)> Keys = new()
    {
        ["Ada"] = (E2EHost.AliceKey, "alice"),
        ["Bo"] = (E2EHost.CarolKey, "carol"),
        ["Kit"] = (E2EHost.OperatorKey, "operator"),
    };

    private IHost _host = null!;
    private Glossary _glossary = null!;
    private Dictionary<string, JsonObject> _irs = null!;
    private HostFile _hostFile = null!;
    private string _directory = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        _glossary = Trax.StateMachine.Features.Glossary.Read(Glossary);
        var derived = FeatureDerivation.Derive(_glossary, Features);
        derived.Problems.Should().BeEmpty();
        _irs = derived.Irs.ToDictionary(kv => kv.Key, kv => JsonNode.Parse(kv.Value)!.AsObject());
        _hostFile = HostFile.Read(derived.Host!);
        _hostFile.Policies.Select(p => p.Shape).Should().BeEquivalentTo(["toSystem", "broadcast"]);

        // Served as from-gherkin writes it, so the policies, the system instance and the views run as on a host.
        _directory = Directory.CreateTempSubdirectory("trax-parity-").FullName;
        File.WriteAllText(Path.Combine(_directory, "host.json"), derived.Host!);
        foreach (var (id, ir) in derived.Irs)
            File.WriteAllText(Path.Combine(_directory, $"{id}.ir.json"), ir);

        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                // The sweep fires due timers; the default would let the shop's close outlast the driver's slack.
                Machines = o =>
                {
                    o.ServeFeatures(_directory);
                    o.InvokeOutcomeSweepInterval = TimeSpan.FromMilliseconds(500);
                    o.InvokeOutcomeSweepIntervalWhileListening = TimeSpan.FromMilliseconds(500);
                },
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public async Task Every_scenario_answers_the_same_in_memory_and_on_the_host()
    {
        var (scenarios, problems) = FeatureFiles.Parse(Features);
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
    public async Task Every_scenario_passes_by_persisted_id_on_a_host_that_requires_them()
    {
        const string database = Database + "_persisted";
        await E2EHost.RecreateDatabaseAsync(database);
        var host = await E2EHost.StartAsync(
            database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Machines = o =>
                {
                    o.ServeFeatures(_directory);
                    o.InvokeOutcomeSweepInterval = TimeSpan.FromMilliseconds(500);
                    o.InvokeOutcomeSweepIntervalWhileListening = TimeSpan.FromMilliseconds(500);
                },
                GraphQL = MachineDocumentOps.Persisted(requirePersisted: true),
            }
        );
        var path = Path.Combine(Path.GetTempPath(), $"trax-parity-{Guid.NewGuid():N}.json");
        try
        {
            await ClientManifestExport.WriteAsync(host.Services, path, CancellationToken.None);
            var manifest = ClientManifest.Read(await File.ReadAllTextAsync(path));
            var (scenarios, problems) = FeatureFiles.Parse(Features);
            problems.Should().BeEmpty();
            var resolver = new StepResolver(_glossary);

            // Without the manifest every operation is a hand-written document, which this host refuses: the step
            // throws, and never reads the refusal as the machine's.
            await FluentActions
                .Awaiting(async () =>
                    await HostDriver("inline", host).Start(Payer("Ada"), "p-order")
                )
                .Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*PERSISTED_OPERATION_REQUIRED*");

            foreach (var scenario in scenarios)
            {
                var result = await ScenarioRunner.Run(
                    Walk.Run(scenario, _glossary, resolver),
                    _glossary,
                    HostDriver(scenario.Name, host, manifest)
                );
                result.Status.Should().Be(ScenarioStatus.Passed, Show(result));
            }

            // A trigger no one is served has no document, so it is not served by id either.
            var rush = HostDriver("rush-persisted", host, manifest);
            (await rush.Start(Payer("Ada"), "p-order")).Should().BeOfType<Answer.Moved>();
            (await rush.Fire(Payer("Ada"), Adas, "Rush", null))
                .Should()
                .BeOfType<Answer.NotServed>();
        }
        finally
        {
            File.Delete(path);
            host.Dispose();
            await E2EHost.DropDatabaseAsync(database);
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

    [Test]
    public async Task Another_owners_instance_is_not_found_on_both()
    {
        var answers = await BothAnswer(
            Payer("Ada"),
            async driver => await driver.Fire(Payer("Bo"), Adas, "Order", Loaves(2))
        );

        answers[0]
            .Should()
            .Be(new Answer.Refused(RefusalCode.NotFound, MemoryDriver.NotFoundMessage));
        answers[1].Should().Be(answers[0]);
    }

    [Test]
    public async Task Another_actors_fire_on_a_users_instance_answers_the_same_on_both()
    {
        var answers = await BothAnswer(
            Payer("Ada"),
            async driver => await driver.Fire(Baker("Kit"), Adas, "Order", Loaves(2))
        );

        answers[0].Should().BeOfType<Answer.Refused>();
        answers[1].Should().Be(answers[0]);
    }

    [Test]
    public async Task A_start_the_rule_does_not_admit_is_forbidden_on_both()
    {
        var answers = await BothAnswer(
            null,
            async driver => await driver.Start(Baker("Kit"), "p-order")
        );

        answers[0]
            .Should()
            .Be(new Answer.Refused(RefusalCode.Forbidden, MemoryDriver.ForbiddenStartMessage));
        answers[1].Should().Be(answers[0]);
    }

    [Test]
    public async Task A_view_the_rule_does_not_admit_is_forbidden_on_both()
    {
        var answers = new List<Answer?>();
        foreach (var driver in Drivers("view"))
            answers.Add((await driver.View(Baker("Kit"), "orders", "status")).Refused);

        answers[0]
            .Should()
            .Be(new Answer.Refused(RefusalCode.Forbidden, MemoryDriver.ForbiddenMessage));
        answers[1].Should().Be(answers[0]);
    }

    [Test]
    public async Task Time_past_the_bound_is_refused_on_the_host_without_waiting()
    {
        var driver = HostDriver("forever");

        await FluentActions
            .Awaiting(async () => await driver.Pass(TimeSpan.FromDays(999)))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*at most 00:10:00*");
    }

    private static readonly InstanceKey Adas = new("p-order", "Ada");

    private Person Payer(string name) => new(name, _glossary.Actors["payer"]);

    private Person Baker(string name) => new(name, _glossary.Actors["baker"]);

    private static JsonObject Loaves(int loaves) => new() { ["loaves"] = loaves };

    private IScenarioDriver[] Drivers(string scenario) =>
        [new MemoryDriver(_irs, _hostFile, _glossary), HostDriver(scenario)];

    // What each driver answers, memory first, after the owner, when there is one, started their order.
    private async Task<List<Answer>> BothAnswer(
        Person? owner,
        Func<IScenarioDriver, Task<Answer>> act
    )
    {
        var answers = new List<Answer>();
        foreach (var driver in Drivers(TestContext.CurrentContext.Test.Name))
        {
            if (owner is not null)
                (await driver.Start(owner, "p-order")).Should().BeOfType<Answer.Moved>();
            answers.Add(await act(driver));
        }
        return answers;
    }

    private HostDriver HostDriver(
        string scenario,
        IHost? host = null,
        ClientManifest? manifest = null
    ) =>
        new(
            new TraxFeatureDriverOptions
            {
                Host = (host ?? _host).Services,
                Client = (host ?? _host).GetTestServer().CreateClient(),
                Manifest = manifest,
                Glossary = FeatureGlossary.Read(Glossary),
                Credential = (_, person, _) =>
                    new FeatureCredential(
                        new Dictionary<string, string> { ["X-Api-Key"] = Keys[person].Key }
                    ),
            },
            _glossary,
            scenario
        );

    private static string Show(ScenarioResult result) =>
        $"{result.Scenario.Name}: "
        + string.Join("; ", result.Steps.Select(s => $"{s.Step.Text} {s.Status} {s.Message}"));
}
