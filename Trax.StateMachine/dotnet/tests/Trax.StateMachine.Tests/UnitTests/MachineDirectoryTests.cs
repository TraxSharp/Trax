using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Ingest.Contracts;
using Trax.Core.Extensions;
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
    private string? _directory;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("trax-machines-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory.NotNull(), recursive: true);

    [Test]
    public async Task A_directory_whose_files_agree_serves_every_machine()
    {
        await Write(Valid()).ConfigureAwait(false);

        var loaded = await IrDirectory
            .LoadAsync(_directory.NotNull(), [typeof(IFetchTrain).Assembly])
            .ConfigureAwait(false);

        loaded.Machines.Select(m => m.Name).Should().BeEquivalentTo(["ingest", "turnstile"]);
        loaded.Host.Policies.Should().ContainSingle();
    }

    [Test]
    public async Task A_machine_the_host_file_serves_with_no_IR_is_refused_naming_it()
    {
        await Write(Valid()).ConfigureAwait(false);
        File.Delete(Path.Combine(_directory.NotNull(), "ingest.ir.json"));

        var load = Load();

        await load.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*serves 'ingest', and there is no ingest.ir.json*")
            .ConfigureAwait(false);
    }

    [Test]
    public async Task An_IR_with_no_entry_in_the_host_file_is_refused_naming_it()
    {
        var host = Valid();
        host["machines"].NotNull().AsObject().Remove("ingest");
        host["policies"] = new JsonArray();
        await Write(host).ConfigureAwait(false);

        var load = Load();

        await load.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ingest.ir.json has no entry in host.json*")
            .ConfigureAwait(false);
    }

    [Test]
    public async Task An_IR_file_not_named_for_its_id_is_refused()
    {
        await Write(Valid()).ConfigureAwait(false);
        File.Move(
            Path.Combine(_directory.NotNull(), "turnstile.ir.json"),
            Path.Combine(_directory.NotNull(), "gate.ir.json")
        );

        var load = Load();

        await load.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*gate.ir.json holds the machine \"turnstile\"*")
            .ConfigureAwait(false);
    }

    [Test]
    public async Task A_policy_waiting_for_a_state_the_machine_does_not_have_is_refused()
    {
        var host = Valid();
        host["policies"].NotNull()[0].NotNull()["when"].NotNull()["enters"] = "Delivered";
        await Write(host).ConfigureAwait(false);

        var load = Load();

        await load.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*policy 0 (toSystem) waits for 'ingest' to enter 'Delivered'*")
            .ConfigureAwait(false);
    }

    [Test]
    public async Task A_rule_for_a_trigger_the_machine_does_not_have_is_refused()
    {
        var host = Valid();
        host["machines"].NotNull()["ingest"].NotNull()["triggers"].NotNull()["Refetch"] =
            new JsonObject { ["anyAuthenticated"] = true };
        await Write(host).ConfigureAwait(false);

        var load = Load();

        await load.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*rule for 'Refetch'*")
            .ConfigureAwait(false);
    }

    [Test]
    public async Task A_train_input_mapped_from_a_field_no_context_has_is_refused()
    {
        var host = Valid();
        host["machines"]
            .NotNull()["ingest"]
            .NotNull()["invokes"]
            .NotNull()["Fetching"]
            .NotNull()["input"]
            .NotNull()["source"] = "bucket";
        await Write(host).ConfigureAwait(false);

        var load = Load();

        await load.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*train input from 'bucket'*")
            .ConfigureAwait(false);
    }

    [Test]
    public async Task A_directory_with_no_host_file_is_refused()
    {
        await Write(Valid()).ConfigureAwait(false);
        File.Delete(Path.Combine(_directory.NotNull(), "host.json"));

        var load = Load();

        await load.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*has no host.json*")
            .ConfigureAwait(false);
    }

    [Test]
    public async Task A_malformed_host_file_is_refused_naming_the_file_and_the_key()
    {
        var host = Valid();
        host["servings"] = new JsonObject();
        await Write(host).ConfigureAwait(false);

        var load = Load();

        await load.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*host.json: host.json /servings:*")
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Two_policies_that_set_each_other_off_refuse_the_host_naming_both()
    {
        var host = Valid();
        host["policies"] = new JsonArray(
            TurnstilePolicy("Unlocked", "Push"),
            TurnstilePolicy("Locked", "Coin")
        );
        await Write(host).ConfigureAwait(false);

        var load = Load();

        await load.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "*policies turnstile.Locked>turnstile.Coin and turnstile.Unlocked>turnstile.Push set each other off*"
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task A_policy_loop_a_timer_closes_is_served()
    {
        // The turnstile locks itself a minute after it opens, and locking sets off the policy that opens it again.
        var turnstile = JsonNode
            .Parse(
                await File.ReadAllTextAsync(FixturePaths.IrFile("turnstile")).ConfigureAwait(false)
            )
            .NotNull()
            .AsObject();
        turnstile["timers"] = new JsonArray(
            new JsonObject
            {
                ["after"] = "PT1M",
                ["rules"] = new JsonArray("undeclared"),
                ["state"] = "Unlocked",
                ["target"] = "Locked",
                ["trigger"] = "Unlocked.after",
            }
        );
        var host = Valid();
        host["machines"].NotNull()["turnstile"].NotNull()["schemaHash"] =
            Trax.StateMachine.Features.MachineFiles.Hash(turnstile);
        host["policies"] = new JsonArray(TurnstilePolicy("Locked", "Coin"));
        await Write(host).ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(_directory.NotNull(), "turnstile.ir.json"),
                turnstile.ToJsonString()
            )
            .ConfigureAwait(false);

        var loaded = await IrDirectory
            .LoadAsync(_directory.NotNull(), [typeof(IFetchTrain).Assembly])
            .ConfigureAwait(false);

        loaded.Host.Policies.Should().ContainSingle();
    }

    [Test]
    public async Task A_policy_loop_through_a_trains_outcome_is_refused()
    {
        // Entering Fetching runs the train, whose outcome moves the instance into Fetched, which retries the fetch.
        var host = Valid();
        host["machines"].NotNull()["ingest"].NotNull()["owner"] = new JsonObject
        {
            ["system"] = "ingest/main",
        };
        host["machines"].NotNull()["ingest"].NotNull().AsObject().Remove("start");
        host["machines"].NotNull()["ingest"].NotNull().AsObject().Remove("read");
        host["machines"].NotNull()["ingest"].NotNull()["triggers"] = new JsonObject
        {
            ["Start"] = new JsonObject { ["server"] = true },
        };
        host["policies"] = new JsonArray(
            new JsonObject
            {
                ["shape"] = "toSystem",
                ["when"] = new JsonObject { ["machine"] = "ingest", ["enters"] = "Fetched" },
                ["then"] = new JsonObject
                {
                    ["machine"] = "ingest",
                    ["key"] = "ingest/main",
                    ["trigger"] = "Start",
                },
            }
        );
        await Write(host).ConfigureAwait(false);

        var load = Load();

        await load.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*policy ingest.Fetched>ingest.Start sets itself off*")
            .ConfigureAwait(false);
    }

    private static JsonObject TurnstilePolicy(string enters, string trigger) =>
        new()
        {
            ["shape"] = "toSystem",
            ["when"] = new JsonObject { ["machine"] = "turnstile", ["enters"] = enters },
            ["then"] = new JsonObject
            {
                ["machine"] = "turnstile",
                ["key"] = "turnstile/main",
                ["trigger"] = trigger,
            },
        };

    private Func<Task> Load() =>
        () => IrDirectory.LoadAsync(_directory.NotNull(), [typeof(IFetchTrain).Assembly]);

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

    private async Task Write(JsonObject host)
    {
        await File.WriteAllTextAsync(
                Path.Combine(_directory.NotNull(), "host.json"),
                host.ToJsonString()
            )
            .ConfigureAwait(false);
        foreach (var name in new[] { "ingest", "turnstile" })
            File.Copy(
                FixturePaths.IrFile(name),
                Path.Combine(_directory.NotNull(), $"{name}.ir.json"),
                overwrite: true
            );
    }

    // The two fixture IRs' schema hashes, read once for the fixture.
    private static readonly Dictionary<string, string> Hashes = new(StringComparer.Ordinal);

    [OneTimeSetUp]
    public async Task HashFixtures()
    {
        foreach (var name in new[] { "ingest", "turnstile" })
            Hashes[name] = Convert.ToHexStringLower(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        CanonicalJson.Serialize(
                            JsonNode
                                .Parse(
                                    await File.ReadAllTextAsync(FixturePaths.IrFile(name))
                                        .ConfigureAwait(false)
                                )
                                .NotNull()
                        )
                    )
                )
            );
    }

    private static string Hash(string name) => Hashes[name];
}
