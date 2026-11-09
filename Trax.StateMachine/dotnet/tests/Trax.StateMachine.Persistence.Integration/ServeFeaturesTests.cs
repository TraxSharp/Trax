using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A host serves the directory <c>from-gherkin</c> writes with one option of <c>AddStateMachines</c> (ADR
/// <c>Trax.Docs/adr/0058</c>): its machines, host file, views and system instances, checked when it registers.
/// </summary>
public class ServeFeaturesTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": { "customer": { "noun": "a customer", "roles": ["customer"] } },
          "machines": {
            "order": {
              "ownedBy": "customer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "states": { "Draft": "is a draft", "Placed": "is placed" },
              "triggers": { "Place": { "phrase": "places {subject}" } }
            }
          }
        }
        """;

    private const string Ordering = """
        Feature: Ordering
          Scenario: Ada places her order
            Given Ada is a customer
            And Ada has an order
            When Ada places her order
            Then Ada's order is placed
        """;

    // An order whose expiry voids the receipt that links it: a linked policy.
    private const string WithPolicy = """
        {
          "version": 1,
          "actors": { "customer": { "noun": "a customer", "roles": ["customer"] } },
          "machines": {
            "order": {
              "ownedBy": "customer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "states": { "Draft": "is a draft", "Expired": "has expired" },
              "triggers": { "Expire": { "phrase": "lets {subject} expire" } }
            },
            "receipt": {
              "ownedBy": "customer",
              "noun": "receipt",
              "start": "has a receipt",
              "initial": "Issued",
              "context": [ { "name": "orderId", "type": "string", "nullable": true, "format": "uuid", "constraints": [] } ],
              "states": { "Issued": "is issued", "Void": "is void" },
              "triggers": { "Cancel": { "phrase": "{subject} is voided" } },
              "links": { "the receipt for {subject}": { "machine": "order", "field": "orderId" } }
            }
          }
        }
        """;

    private const string Expiring = """
        Feature: Expiring
          Background:
            Given Ada is a customer
            And Ada has an order
            And Ada has a receipt

          Scenario: A receipt is voided
            When Ada's receipt is voided
            Then Ada's receipt is void

          Scenario: An expired order voids its receipt
            When Ada lets her order expire
            Then Ada's order has expired
            And Ada's receipt is void
        """;

    // A tally whose Scoring state invokes a train returning Dictionary<string, int>; {range} is the values' range.
    private const string Scoring = """
        {
          "version": 1,
          "actors": { "customer": { "noun": "a customer", "roles": ["customer"] } },
          "machines": {
            "tally": {
              "ownedBy": "customer",
              "noun": "tally",
              "start": "has a tally",
              "initial": "Draft",
              "states": { "Draft": "is a draft", "Scoring": "is being scored", "Scored": "is scored", "Unscored": "is unscored" },
              "triggers": { "Score": { "phrase": "scores {subject}" } },
              "invokes": {
                "Scoring": {
                  "train": "Trax.StateMachine.Persistence.Integration.IScoreTallyTrain",
                  "output": [
                    { "name": "totals", "type": "object", "nullable": false, "constraints": [],
                      "values": { "type": "integer", "nullable": false{range} } }
                  ],
                  "outcomes": {
                    "the answers are scored": { "outcome": "done" },
                    "the scoring fails": { "outcome": "failed" },
                    "the scoring is called off": { "outcome": "cancelled" }
                  },
                  "examples": { "the answers are scored": { "totals": { "ada": 1 } } }
                }
              }
            }
          }
        }
        """;

    private static string Scored(string outcome, string then) =>
        $"""

              Scenario: {outcome}
                When Ada scores her tally
                Then Ada's tally is being scored
                When {outcome}
                Then Ada's tally {then}
            """;

    private static readonly string Tallying =
        "Feature: Tallying\n  Background:\n    Given Ada is a customer\n    And Ada has a tally\n"
        + Scored("the answers are scored", "is scored")
        + Scored("the scoring fails", "is unscored")
        + Scored("the scoring is called off", "is unscored");

    private string _dir = null!;

    [SetUp]
    public void SetUp() => _dir = Write(Glossary, Ordering);

    private static string Write(string glossary, string feature)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-serve-features-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var derived = FeatureMachines.Derive(
            FeatureGlossary.Read(glossary),
            [new FeatureFile("machines.feature", feature)]
        );
        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        foreach (var (id, ir) in derived.Irs)
            File.WriteAllText(Path.Combine(dir, id + ".ir.json"), ir);
        File.WriteAllText(Path.Combine(dir, "host.json"), derived.Host);
        return dir;
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    [Test]
    public void The_directorys_machines_and_host_file_are_registered()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax =>
            trax.AddEffects(e => e.UseInMemory()).AddStateMachines(o => o.ServeFeatures(_dir))
        );

        services
            .Where(d => d.ServiceType == typeof(IMachine))
            .Select(d => (d.ImplementationInstance as IMachine)?.Name)
            .Should()
            .Contain("order");
        services.Should().Contain(d => d.ServiceType == typeof(HostFile));
    }

    [Test]
    public void A_directory_whose_host_file_was_derived_from_another_ir_is_refused()
    {
        var ir = Path.Combine(_dir, "order.ir.json");
        File.WriteAllText(
            ir,
            File.ReadAllText(ir)
                .Replace("\"committedStates\":[]", "\"committedStates\":[\"Placed\"]")
        );
        var services = new ServiceCollection();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(e => e.UseInMemory()).AddStateMachines(o => o.ServeFeatures(_dir))
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*derived from another IR*");
    }

    [Test]
    public void A_policy_runner_without_features_is_refused()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(e => e.UseInMemory())
                    .AddStateMachines(o => o.PolicyRunner(p => p.BatchSize = 10))
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ServeFeatures was not called*");
    }

    [Test]
    public void Policies_on_the_InMemory_provider_are_refused()
    {
        var dir = Write(WithPolicy, Expiring);
        try
        {
            var services = new ServiceCollection();

            var act = () =>
                services.AddTrax(trax =>
                    trax.AddEffects(e => e.UseInMemory())
                        .AddStateMachines(o => o.ServeFeatures(dir))
                );

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*policies*InMemory*UsePostgres(...) or UseSqlite(...)*");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestCase("BatchSize", 0)]
    [TestCase("BatchSize", -1)]
    [TestCase("FanOutCap", -1)]
    [TestCase("FanOutCap", int.MaxValue)]
    [TestCase("Interval", 0)]
    [TestCase("Interval", -1)]
    [TestCase("Interval", 50 * 24 * 3600)]
    public void A_policy_runner_option_out_of_range_is_refused_naming_it(string option, int value)
    {
        var dir = Write(WithPolicy, Expiring);
        try
        {
            var services = new ServiceCollection();
            var file = Path.Combine(dir, "store.db");

            var act = () =>
                services.AddTrax(trax =>
                    trax.AddEffects(e => e.UseSqlite($"Data Source={file}"))
                        .AddStateMachines(o =>
                            o.ServeFeatures(dir)
                                .PolicyRunner(p =>
                                {
                                    switch (option)
                                    {
                                        case "BatchSize":
                                            p.BatchSize = value;
                                            break;
                                        case "FanOutCap":
                                            p.FanOutCap = value;
                                            break;
                                        default:
                                            p.Interval = TimeSpan.FromSeconds(value);
                                            break;
                                    }
                                })
                        )
                );

            act.Should()
                .Throw<ArgumentOutOfRangeException>()
                .WithMessage($"*PolicyRunnerOptions.{option}*");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void ServeFeatures_names_the_output_range_a_train_needs()
    {
        var dir = Write(Scoring.Replace("{range}", ""), Tallying);
        try
        {
            var services = new ServiceCollection();

            var act = () =>
                services.AddTrax(trax =>
                    trax.AddEffects(e => e.UseInMemory())
                        .AddStateMachines(o =>
                            o.ServeFeatures(dir, typeof(IScoreTallyTrain).Assembly)
                        )
                );

            var refusal = act.Should().Throw<InvalidOperationException>().Which.Message;
            refusal.Should().Contain("The machine 'tally'");
            refusal.Should().Contain(typeof(IScoreTallyTrain).FullName);
            refusal
                .Should()
                .Contain(
                    "invokes.Scoring.output.totals.values has no \"maximum\"; write \"maximum\": 2147483647"
                );
            refusal
                .Should()
                .Contain(
                    "invokes.Scoring.output.totals.values has no \"minimum\"; write \"minimum\": -2147483648"
                );
            refusal.Should().NotContain("First difference");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void ServeFeatures_serves_a_train_output_whose_range_the_glossary_gives()
    {
        var dir = Write(
            Scoring.Replace("{range}", ", \"minimum\": -2147483648, \"maximum\": 2147483647"),
            Tallying
        );
        try
        {
            var services = new ServiceCollection();

            services.AddTrax(trax =>
                trax.AddEffects(e => e.UseInMemory())
                    .AddStateMachines(o => o.ServeFeatures(dir, typeof(IScoreTallyTrain).Assembly))
            );

            services
                .Select(d => (d.ImplementationInstance as IMachine)?.Name)
                .Should()
                .Contain("tally");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void Serving_two_directories_is_refused()
    {
        var options = new StateMachineOptions().ServeFeatures(_dir);

        options.Invoking(o => o.ServeFeatures(_dir)).Should().Throw<InvalidOperationException>();
    }
}

/// <summary>The input of the train the tally's Scoring state invokes.</summary>
public sealed record ScoreTallyInput;

/// <summary>Its output: an integer per name, so the glossary's values need an int's range.</summary>
public sealed record ScoreTallyOutput(Dictionary<string, int> Totals);

/// <summary>The train, named by its interface; registering the machine never runs it.</summary>
public interface IScoreTallyTrain : IServiceTrain<ScoreTallyInput, ScoreTallyOutput>;
