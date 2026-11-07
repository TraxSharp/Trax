using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
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

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-serve-features-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        var derived = FeatureMachines.Derive(
            FeatureGlossary.Read(Glossary),
            [new FeatureFile("ordering.feature", Ordering)]
        );
        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        foreach (var (id, ir) in derived.Irs)
            File.WriteAllText(Path.Combine(_dir, id + ".ir.json"), ir);
        File.WriteAllText(Path.Combine(_dir, "host.json"), derived.Host);
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
        File.WriteAllText(ir, File.ReadAllText(ir).Replace("\"version\":1", "\"version\":2"));
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
    public void Serving_two_directories_is_refused()
    {
        var options = new StateMachineOptions().ServeFeatures(_dir);

        options.Invoking(o => o.ServeFeatures(_dir)).Should().Throw<InvalidOperationException>();
    }
}
