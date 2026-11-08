using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Reqnroll;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Gherkin.Reqnroll;
using FeatureScenario = Trax.StateMachine.Gherkin.Reqnroll.FeatureScenario;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A test project's own steps beside the feature bindings: the glossary reads a step first, a registered step runs
/// only when no phrase matches, and it acts as the scenario's person, with the credential and the instances the
/// glossary's steps use. A Then nothing reads is pending in a machine feature and fails in a spec (ADR
/// <c>Trax.Docs/adr/0058</c>).
/// </summary>
[TestFixture]
[NonParallelizable]
public class CustomStepsTests
{
    private const string Database = "trax_sm_custom_steps";

    private const string Glossary = """
        {
          "version": 1,
          "actors": {
            "payer": { "noun": "a payer", "roles": ["Payer"] }
          },
          "machines": {
            "c-order": {
              "ownedBy": "payer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "context": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
              "states": { "Draft": "is a draft" },
              "triggers": {
                "Order": {
                  "phrase": "orders {loaves} loaves for {subject}",
                  "input": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "loaves", "value": { "input": "loaves" } } ] }
                }
              }
            }
          }
        }
        """;

    private const string Ordering = """
        Feature: Ordering
          Scenario: Ordering
            Given Ada is a payer
            And Ada has an order
            When Ada orders "2" loaves for her order
            Then Ada's order is a draft
        """;

    private static readonly Dictionary<string, string> Keys = new()
    {
        ["Ada"] = E2EHost.AliceKey,
        ["Bo"] = E2EHost.CarolKey,
    };

    private IHost _host = null!;
    private string _directory = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        var derived = FeatureMachines.Derive(
            FeatureGlossary.Read(Glossary),
            [new FeatureFile("ordering.feature", Ordering)]
        );
        derived.Problems.Should().BeEmpty();
        _directory = Directory.CreateTempSubdirectory("trax-custom-steps-").FullName;
        File.WriteAllText(Path.Combine(_directory, "host.json"), derived.Host!);
        foreach (var (id, ir) in derived.Irs)
            File.WriteAllText(Path.Combine(_directory, $"{id}.ir.json"), ir);

        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions { Machines = o => o.ServeFeatures(_directory) }
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
    public async Task A_registered_Then_reads_the_host_as_the_scenarios_person()
    {
        var options = Options()
            .Then(
                @"(?<person>[A-Z][a-z]+)'s order holds (?<n>\d+) loaves on the host",
                async (scenario, match) =>
                {
                    var person = match.Groups["person"].Value;
                    var id = scenario.InstanceOf(person, "c-order");
                    id.Should().NotBeNull();
                    var order = await Get(scenario, person, id!.Value);
                    order.Should().NotBeNull("the order is the person's own");
                    order!["problem"].Should().BeNull();
                    order["context"]!["loaves"]!
                        .GetValue<int>()
                        .Should()
                        .Be(int.Parse(match.Groups["n"].Value));
                }
            )
            .Then(
                @"Bo cannot read Ada's order on the host",
                async (scenario, _) =>
                {
                    scenario.InstanceOf("Bo", "c-order").Should().BeNull();
                    var order = await Get(
                        scenario,
                        "Bo",
                        scenario.InstanceOf("Ada", "c-order")!.Value
                    );
                    order.Should().BeNull("another user's id is answered as an unknown one");
                }
            );
        var run = Run(options, machineFeature: false);

        await run.Step(StepKind.Given, "Ada is a payer");
        await run.Step(StepKind.Given, "Bo is a payer");
        await run.Step(StepKind.Given, "Ada has an order");
        await run.Step(StepKind.When, "Ada orders \"2\" loaves for her order");
        await run.Step(StepKind.Then, "Ada's order holds 2 loaves on the host");
        await run.Step(StepKind.Then, "Bo cannot read Ada's order on the host");
        await FluentActions
            .Awaiting(() => run.Step(StepKind.Then, "Ada's order holds 3 loaves on the host"))
            .Should()
            .ThrowAsync<Exception>();
    }

    [Test]
    public async Task A_registered_step_gets_the_persons_credential_and_the_scenarios_bag()
    {
        string? header = null;
        var options = Options()
            .When(
                @"Ada notes (?<n>\d+)",
                (scenario, match) =>
                {
                    header = scenario.Credential("Ada").Headers["X-Api-Key"];
                    scenario.Items["note"] = match.Groups["n"].Value;
                    return Task.CompletedTask;
                }
            )
            .Then(
                @"the note is (?<n>\d+)",
                (scenario, match) =>
                {
                    scenario.Items["note"].Should().Be(match.Groups["n"].Value);
                    return Task.CompletedTask;
                }
            );
        var run = Run(options, machineFeature: false);

        await run.Step(StepKind.Given, "Ada is a payer");
        await run.Step(StepKind.When, "Ada notes 7");
        await run.Step(StepKind.Then, "the note is 7");
        header.Should().Be(E2EHost.AliceKey);
    }

    [Test]
    public async Task A_person_the_scenario_did_not_introduce_has_no_credential()
    {
        var options = Options()
            .When(
                @"Cy notes it",
                (scenario, _) =>
                {
                    scenario.Credential("Cy");
                    return Task.CompletedTask;
                }
            );
        var run = Run(options, machineFeature: false);

        await FluentActions
            .Awaiting(() => run.Step(StepKind.When, "Cy notes it"))
            .Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*Cy is not a person of this scenario*");
    }

    [Test]
    public async Task A_Then_nothing_reads_is_pending_in_a_machine_feature_and_fails_in_a_spec()
    {
        var machine = Run(Options(), machineFeature: true);
        await FluentActions
            .Awaiting(() => machine.Step(StepKind.Then, "Ada sees a green tick"))
            .Should()
            .ThrowAsync<PendingStepException>();

        var spec = Run(Options(), machineFeature: false);
        var thrown = await FluentActions
            .Awaiting(() => spec.Step(StepKind.Then, "Ada sees a green tick"))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*no registered Then says*");
        thrown.Which.Should().NotBeOfType<PendingStepException>();
    }

    [Test]
    public async Task A_When_nothing_reads_fails_in_a_machine_feature_too()
    {
        var run = Run(Options(), machineFeature: true);

        await FluentActions
            .Awaiting(() => run.Step(StepKind.When, "Ada requeues the dead letter"))
            .Should()
            .ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task A_step_two_registered_patterns_match_is_ambiguous()
    {
        var options = Options()
            .When(@"Ada requeues .*", (_, _) => Task.CompletedTask)
            .When(@".* the dead letter", (_, _) => Task.CompletedTask);
        var run = Run(options, machineFeature: false);

        await FluentActions
            .Awaiting(() => run.Step(StepKind.When, "Ada requeues the dead letter"))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ambiguous*");
    }

    [Test]
    public async Task The_glossary_reads_a_step_before_a_registered_one()
    {
        var options = Options()
            .Then(
                @"Ada's order is a draft",
                (_, _) => throw new InvalidOperationException("the registered step ran")
            );
        var run = Run(options, machineFeature: false);

        await run.Step(StepKind.Given, "Ada is a payer");
        await run.Step(StepKind.Given, "Ada has an order");
        await run.Step(StepKind.Then, "Ada's order is a draft");
    }

    [Test]
    public async Task A_registered_step_cannot_speak_as_I()
    {
        var options = Options().Then(@"I see my order", (_, _) => Task.CompletedTask);
        var run = Run(options, machineFeature: true);

        await FluentActions
            .Awaiting(() => run.Step(StepKind.Then, "I see my order"))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*speaks as 'I'*");
    }

    [Test]
    public void A_pattern_registered_twice_or_malformed_is_refused()
    {
        var options = Options().Given(@"Ada waits", (_, _) => Task.CompletedTask);

        FluentActions
            .Invoking(() => options.Given(@"Ada waits", (_, _) => Task.CompletedTask))
            .Should()
            .Throw<ArgumentException>();
        FluentActions
            .Invoking(() => options.Then(@"Ada (waits", (_, _) => Task.CompletedTask))
            .Should()
            .Throw<ArgumentException>();
    }

    [TestCase("features", true)]
    [TestCase("Features", true)]
    [TestCase("../../samples/Bakery/features", true)]
    [TestCase(@"..\..\samples\Bakery\features\", true)]
    [TestCase("features/checkout", true)]
    [TestCase(@"Features\checkout\pay", true)]
    [TestCase("../../samples/Bakery/features/checkout", true)]
    [TestCase("specs", false)]
    [TestCase("Specs", false)]
    [TestCase("specs/checkout", false)]
    [TestCase("featuresx", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void A_feature_under_a_folder_named_features_is_a_machine_feature(
        string? folder,
        bool machine
    ) => HostFeatureRun.IsMachineFeature(folder).Should().Be(machine);

    [TestCase("features/specs")]
    [TestCase("specs/features")]
    [TestCase(@"Specs\checkout\Features")]
    public void A_feature_under_both_a_features_and_a_specs_folder_is_refused(string folder) =>
        FluentActions
            .Invoking(() => HostFeatureRun.IsMachineFeature(folder))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*both a features folder and a specs folder*");

    private TraxFeatureDriverOptions Options() =>
        new()
        {
            Host = _host.Services,
            Client = _host.GetTestServer().CreateClient(),
            Glossary = FeatureGlossary.Read(Glossary),
            Credential = (_, person, _) =>
                new FeatureCredential(
                    new Dictionary<string, string> { ["X-Api-Key"] = Keys[person] }
                ),
        };

    private static HostFeatureRun Run(TraxFeatureDriverOptions options, bool machineFeature) =>
        new(
            options,
            $"custom/{TestContext.CurrentContext.Test.Name}/{Guid.NewGuid():N}",
            machineFeature
        );

    // The person's read of an order, as a client sends it: what get answered, null for an order the person cannot see.
    // A read the host answers with errors fails here, so it is never taken for that null.
    private static async Task<JsonNode?> Get(FeatureScenario scenario, string person, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(
                new
                {
                    query = $"{{ machines {{ cOrder {{ get(id: \"{id}\") {{ state context {{ loaves }} problem {{ code message }} }} }} }} }}",
                }
            ),
        };
        using var response = await scenario.SendAsync(person, request);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body["errors"].Should().BeNull("the host answers the read");
        var orders = body["data"]!["machines"]!["cOrder"]!.AsObject();
        orders.ContainsKey("get").Should().BeTrue();
        return orders["get"];
    }
}
