using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Ingest.Contracts;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A directory of machines derived from features, one <c>&lt;id&gt;.ir.json</c> per machine and one <c>host.json</c>, is
/// read whole and checked before anything is served (ADR <c>Trax.Docs/adr/0058</c>): the host file and the IRs list the
/// same machines, and every state, trigger and field the host file names is in the IRs. A mispaired or stale file is
/// refused, naming it, rather than applied by name.
/// </summary>
public class MachineDirectoryTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("trax-machines-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void A_directory_whose_files_agree_serves_every_machine()
    {
        Write(Valid());

        var loaded = IrDirectory.Load(_directory, [typeof(IFetchTrain).Assembly]);

        loaded.Machines.Select(m => m.Name).Should().BeEquivalentTo(["ingest", "turnstile"]);
        loaded.Host.Policies.Should().ContainSingle();
    }

    [Test]
    public void A_machine_the_host_file_serves_with_no_IR_is_refused_naming_it()
    {
        Write(Valid());
        File.Delete(Path.Combine(_directory, "ingest.ir.json"));

        var load = Load();

        load.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*serves 'ingest', and there is no ingest.ir.json*");
    }

    [Test]
    public void An_IR_with_no_entry_in_the_host_file_is_refused_naming_it()
    {
        var host = Valid();
        host["machines"]!.AsObject().Remove("ingest");
        host["policies"] = new JsonArray();
        Write(host);

        var load = Load();

        load.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ingest.ir.json has no entry in host.json*");
    }

    [Test]
    public void An_IR_file_not_named_for_its_id_is_refused()
    {
        Write(Valid());
        File.Move(
            Path.Combine(_directory, "turnstile.ir.json"),
            Path.Combine(_directory, "gate.ir.json")
        );

        var load = Load();

        load.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*gate.ir.json holds the machine \"turnstile\"*");
    }

    [Test]
    public void A_policy_waiting_for_a_state_the_machine_does_not_have_is_refused()
    {
        var host = Valid();
        host["policies"]![0]!["when"]!["enters"] = "Delivered";
        Write(host);

        var load = Load();

        load.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*policy 0 (toSystem) waits for 'ingest' to enter 'Delivered'*");
    }

    [Test]
    public void A_rule_for_a_trigger_the_machine_does_not_have_is_refused()
    {
        var host = Valid();
        host["machines"]!["ingest"]!["triggers"]!["Refetch"] = new JsonObject
        {
            ["anyAuthenticated"] = true,
        };
        Write(host);

        var load = Load();

        load.Should().Throw<InvalidOperationException>().WithMessage("*rule for 'Refetch'*");
    }

    [Test]
    public void A_train_input_mapped_from_a_field_no_context_has_is_refused()
    {
        var host = Valid();
        host["machines"]!["ingest"]!["invokes"]!["Fetching"]!["input"]!["source"] = "bucket";
        Write(host);

        var load = Load();

        load.Should().Throw<InvalidOperationException>().WithMessage("*train input from 'bucket'*");
    }

    [Test]
    public void A_directory_with_no_host_file_is_refused()
    {
        Write(Valid());
        File.Delete(Path.Combine(_directory, "host.json"));

        var load = Load();

        load.Should().Throw<InvalidOperationException>().WithMessage("*has no host.json*");
    }

    [Test]
    public void A_malformed_host_file_is_refused_naming_the_file_and_the_key()
    {
        var host = Valid();
        host["servings"] = new JsonObject();
        Write(host);

        var load = Load();

        load.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*host.json: host.json /servings:*");
    }

    private Action Load() => () => IrDirectory.Load(_directory, [typeof(IFetchTrain).Assembly]);

    // The ingest machine, served to its users, and the turnstile as a system machine it opens when a fetch is approved.
    private static JsonObject Valid()
    {
        var any = new JsonObject { ["anyAuthenticated"] = true };
        var server = new JsonObject { ["server"] = true };
        return new JsonObject
        {
            ["version"] = 1,
            ["machines"] = new JsonObject
            {
                ["ingest"] = new JsonObject
                {
                    ["schemaHash"] = Hash("ingest"),
                    ["owner"] = "user",
                    ["start"] = any.DeepClone(),
                    ["read"] = any.DeepClone(),
                    ["triggers"] = new JsonObject
                    {
                        ["Start"] = any.DeepClone(),
                        ["Retry"] = any.DeepClone(),
                        ["Abandon"] = any.DeepClone(),
                        ["Approve"] = any.DeepClone(),
                    },
                    ["invokes"] = new JsonObject
                    {
                        ["Fetching"] = new JsonObject
                        {
                            ["train"] = typeof(IFetchTrain).FullName,
                            ["input"] = new JsonObject { ["source"] = "source" },
                        },
                    },
                },
                ["turnstile"] = new JsonObject
                {
                    ["schemaHash"] = Hash("turnstile"),
                    ["owner"] = new JsonObject { ["system"] = "turnstile/main" },
                    ["triggers"] = new JsonObject
                    {
                        ["Coin"] = server.DeepClone(),
                        ["Push"] = server.DeepClone(),
                    },
                },
            },
            ["policies"] = new JsonArray(
                new JsonObject
                {
                    ["shape"] = "toSystem",
                    ["when"] = new JsonObject { ["machine"] = "ingest", ["enters"] = "Approved" },
                    ["then"] = new JsonObject
                    {
                        ["machine"] = "turnstile",
                        ["key"] = "turnstile/main",
                        ["trigger"] = "Push",
                    },
                }
            ),
        };
    }

    private void Write(JsonObject host)
    {
        File.WriteAllText(Path.Combine(_directory, "host.json"), host.ToJsonString());
        foreach (var name in new[] { "ingest", "turnstile" })
            File.Copy(
                FixturePaths.IrFile(name),
                Path.Combine(_directory, $"{name}.ir.json"),
                overwrite: true
            );
    }

    private static string Hash(string name) =>
        Convert.ToHexStringLower(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    CanonicalJson.Serialize(
                        JsonNode.Parse(File.ReadAllText(FixturePaths.IrFile(name)))!
                    )
                )
            )
        );
}
