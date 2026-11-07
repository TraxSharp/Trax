using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Ingest.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A machine served from its IR file and its host entry, with no C# class (ADR <c>Trax.Docs/adr/0058</c>). Every
/// conformance machine a file can describe is served this way and must export exactly its file and replay its corpus as
/// the hand-written machine does; anything a file cannot describe, and any file that does not match, is refused at
/// registration, naming the machine.
/// </summary>
public class MachineFromIrTests
{
    private static readonly Assembly[] Trains = [typeof(IFetchTrain).Assembly];

    private static readonly AccessRule Editors = new([
        ["Editor"],
    ]);

    // The access the hand-written conformance machines declare, as their host entries say it. A timer's rules are
    // computed from it and are in the IR, so an entry that disagrees exports a different IR.
    private static readonly Dictionary<string, Func<JsonObject, HostMachine>> Hosts = new()
    {
        ["turnstile"] = ir => Host(ir),
        ["gauge"] = ir => Host(ir),
        ["reminder"] = ir =>
            Host(ir) with
            {
                Triggers = new Dictionary<string, TriggerAccess>
                {
                    ["Open"] = new(Editors),
                    ["Extend"] = new(AccessRule.AnyAuthenticated),
                },
            },
        ["ingest"] = ir => Host(ir) with { Invokes = Fetching("Fetching") },
        ["wait"] = ir => Host(ir) with { Invokes = Fetching("Fetching") },
    };

    public static IEnumerable<string> Served => Hosts.Keys;

    [TestCaseSource(nameof(Served))]
    public void A_conformance_machine_served_from_its_IR_exports_exactly_its_file(string name)
    {
        var ir = Ir(name);

        var machine = IrMachines.Create(new IrMachines.Source(ir, Hosts[name](ir)), Trains);

        machine.Name.Should().Be(name);
        machine.ExportIr().Should().Be(CanonicalJson.Serialize(ir));
        machine.SchemaHash.Should().Be(Hash(ir));
    }

    [TestCaseSource(nameof(WithCorpus))]
    public void A_conformance_machine_served_from_its_IR_replays_its_corpus(string name)
    {
        var ir = Ir(name);
        var corpus = File.ReadAllText(FixturePaths.DifferentialFile(name));

        var machine = IrMachines.Create(new IrMachines.Source(ir, Hosts[name](ir), corpus), Trains);

        machine
            .SelfCheck()
            .Should()
            .BeEmpty($"the {name} machine served from its IR answers its corpus");
    }

    public static IEnumerable<string> WithCorpus =>
        Served.Where(n => File.Exists(FixturePaths.DifferentialFile(n)));

    [Test]
    public void A_users_machine_is_served_to_clients_and_a_system_machine_is_not()
    {
        var ir = Ir("turnstile");
        var user = IrMachines.Create(new IrMachines.Source(ir, Host(ir)), Trains);
        var system = IrMachines.Create(
            new IrMachines.Source(ir, Host(ir) with { SystemKey = "turnstile/main" }),
            Trains
        );

        user.GetType().IsDefined(typeof(TraxMachineAttribute), inherit: false).Should().BeTrue();
        system.GetType().IsDefined(typeof(TraxMachineAttribute), inherit: false).Should().BeFalse();
        ((IMachineInternals)system).SystemOwned.Should().BeTrue();
    }

    [Test]
    public void A_host_entry_derived_from_another_IR_is_refused()
    {
        var ir = Ir("turnstile");

        var create = () =>
            IrMachines.Create(
                new IrMachines.Source(ir, Host(ir) with { SchemaHash = "0123" }),
                Trains
            );

        create
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'turnstile'*derived from the IR 0123*");
    }

    [Test]
    public void A_host_entry_for_another_machine_is_refused()
    {
        var ir = Ir("turnstile");

        var create = () =>
            IrMachines.Create(new IrMachines.Source(ir, Host(ir) with { Id = "gauge" }), Trains);

        create
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'gauge'*IR's id is*turnstile*");
    }

    [Test]
    public void Access_that_disagrees_with_the_IRs_timer_rules_is_refused()
    {
        var ir = Ir("reminder");
        var host = Hosts["reminder"](ir) with
        {
            Triggers = new Dictionary<string, TriggerAccess> { ["Open"] = new(Editors) },
        };

        var create = () => IrMachines.Create(new IrMachines.Source(ir, host), Trains);

        create
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'reminder'*exports a different IR*rules*");
    }

    [Test]
    public void An_edge_that_runs_an_effect_is_refused()
    {
        var ir = Ir("checkout");

        var create = () => IrMachines.Create(new IrMachines.Source(ir, Host(ir)), Trains);

        create
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'checkout'*runs an effect*");
    }

    [Test]
    public void A_custom_rule_is_refused_wherever_it_nests()
    {
        var ir = Ir("vault");

        var create = () => IrMachines.Create(new IrMachines.Source(ir, Host(ir)), Trains);

        create
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'vault'*custom rule or reduction*");
    }

    [Test]
    public void An_invoking_state_with_no_train_in_the_host_entry_is_refused()
    {
        var ir = Ir("ingest");

        var create = () => IrMachines.Create(new IrMachines.Source(ir, Host(ir)), Trains);

        create
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'ingest'*Fetching invokes a train*");
    }

    [Test]
    public void A_train_no_train_assembly_holds_is_refused()
    {
        var ir = Ir("ingest");
        var host = Host(ir) with
        {
            Invokes = new Dictionary<string, HostInvoke>
            {
                ["Fetching"] = new(
                    "Fetching",
                    "Ingest.Contracts.IMissingTrain",
                    new Dictionary<string, string>()
                ),
            },
        };

        var create = () => IrMachines.Create(new IrMachines.Source(ir, host), Trains);

        create
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*IMissingTrain, which none*");
    }

    [Test]
    public void A_user_machines_server_only_trigger_is_not_served_and_needs_no_client_rule()
    {
        var ir = Ir("turnstile");
        var host = Host(ir) with
        {
            Start = AccessRule.AnyAuthenticated,
            Read = AccessRule.AnyAuthenticated,
            Triggers = new Dictionary<string, TriggerAccess>
            {
                ["Coin"] = new(AccessRule.AnyAuthenticated),
                ["Push"] = TriggerAccess.Server,
            },
        };

        var machine = IrMachines.Create(new IrMachines.Source(ir, host), Trains);

        machine
            .Triggers.Select(t => t.Name)
            .Should()
            .NotContain("Push", "a server-only trigger is not in the typed API");
        ((IMachineInternals)machine).ServerTriggers.Should().Equal("Push");
        MachineRulesStartupValidator
            .Problems([machine], new StateMachineOptions(), null)
            .Result.Should()
            .BeEmpty();
        machine
            .ExportIr()
            .Should()
            .Be(CanonicalJson.Serialize(ir), "the IR still has Push; only its serving changes");
    }

    [Test]
    public void A_server_only_trigger_into_an_invoking_state_of_a_user_machine_refuses_startup()
    {
        var ir = Ir("ingest");
        var host = Hosts["ingest"](ir) with
        {
            Triggers = new Dictionary<string, TriggerAccess>
            {
                ["Start"] = TriggerAccess.Server,
                ["Abandon"] = new(AccessRule.AnyAuthenticated),
                ["Approve"] = new(AccessRule.AnyAuthenticated),
                ["Retry"] = new(AccessRule.AnyAuthenticated),
            },
        };
        var machine = IrMachines.Create(new IrMachines.Source(ir, host), Trains);

        var problems = InvokesStartupValidator.Problems(
            machine,
            new ServiceCollection().BuildServiceProvider()
        );

        problems
            .Should()
            .Contain(
                p => p.Contains("Start enters Fetching"),
                "a policy firing Start would queue the run with no user present to authorize it"
            );
    }

    [Test]
    public void A_server_only_trigger_into_an_invoking_state_of_a_system_machine_is_allowed()
    {
        var ir = Ir("ingest");
        var host = Hosts["ingest"](ir) with
        {
            SystemKey = "ingest/main",
            Triggers = new Dictionary<string, TriggerAccess> { ["Start"] = TriggerAccess.Server },
        };
        var machine = IrMachines.Create(new IrMachines.Source(ir, host), Trains);

        InvokesStartupValidator
            .Problems(machine, new ServiceCollection().BuildServiceProvider())
            .Should()
            .NotContain(p => p.Contains("Start enters"));
    }

    [Test]
    public void A_field_whose_name_no_property_can_have_is_refused()
    {
        var ir = Ir("turnstile");
        ir["context"] = new JsonObject
        {
            ["Locked"] = new JsonObject
            {
                ["fields"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = "two words",
                        ["type"] = "string",
                        ["nullable"] = true,
                        ["constraints"] = new JsonArray(),
                    }
                ),
            },
        };

        var create = () => IrMachines.Create(new IrMachines.Source(ir, Host(ir)), Trains);

        create
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'two words'*not a name a property can have*");
    }

    [Test]
    public void Two_machines_whose_types_would_share_a_name_are_refused_naming_both()
    {
        var first = Renamed(Ir("turnstile"), "a-1b");
        var second = Renamed(Ir("turnstile"), "a1b");

        var create = () =>
            IrMachines.Create(
                [
                    new IrMachines.Source(first, Host(first)),
                    new IrMachines.Source(second, Host(second)),
                ],
                Trains
            );

        create.Should().Throw<InvalidOperationException>().WithMessage("*'a-1b' and 'a1b'*A1b*");
    }

    [Test]
    public void The_emitted_records_carry_each_fields_JSON_name()
    {
        var ir = Ir("ingest");

        var machine = IrMachines.Create(new IrMachines.Source(ir, Hosts["ingest"](ir)), Trains);

        var context = machine.States.Single(s => s.Name == "Fetched").ContextType!;
        context
            .GetProperty("Fingerprint")!
            .GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()!
            .Name.Should()
            .Be("fingerprint");
    }

    private static JsonObject Ir(string name) =>
        JsonNode.Parse(File.ReadAllText(FixturePaths.IrFile(name)))!.AsObject();

    private static JsonObject Renamed(JsonObject ir, string id)
    {
        ir["id"] = id;
        return ir;
    }

    private static HostMachine Host(JsonObject ir) =>
        new(
            ir["id"]!.GetValue<string>(),
            Hash(ir),
            null,
            null,
            null,
            new Dictionary<string, TriggerAccess>(),
            new Dictionary<string, HostInvoke>()
        );

    private static Dictionary<string, HostInvoke> Fetching(string state) =>
        new()
        {
            [state] = new(
                state,
                typeof(IFetchTrain).FullName!,
                new Dictionary<string, string> { ["source"] = "source" }
            ),
        };

    private static string Hash(JsonObject ir) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(ir)))
        );
}
