using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Cli.Clients;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;
using Trax.Effect.StateMachine;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax client</c> renders a frontend's client from a host's manifest: every module checks the client format
/// before anything else, a machine's documents are sent by id, a train gets a function only from a document the
/// frontend wrote, its input and answer typed from the manifest's SDL, anything from the manifest is written as data (names checked, strings as literals,
/// comments unable to close), the same manifest writes the same bytes, and <c>--check</c> reports what differs.
/// </summary>
public partial class ClientGeneratorTests
{
    private string _tools = null!;
    private string _out = null!;

    [SetUp]
    public void SetUp()
    {
        _tools = Path.Combine(Path.GetTempPath(), $"trax-client-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tools);
        File.WriteAllText(Path.Combine(_tools, "generate-twin.mjs"), "");
        _out = Path.Combine(Path.GetTempPath(), $"trax-client-out-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var dir in new[] { _tools, _out })
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
    }

    private IReadOnlyList<ClientFile> Render(
        ClientManifest? manifest = null,
        FakeNodeRunner? node = null
    ) =>
        new ClientGenerator(node ?? new FakeNodeRunner()).Render(
            new ClientGenerateOptions(manifest ?? ClientManifests.Sample(), ToolsDir: _tools)
        );

    private static string Text(IReadOnlyList<ClientFile> files, string path) =>
        files.Single(f => f.Path == path).Text;

    private static readonly ClientDocumentFile StartRunFile = new(
        "src/startRun.graphql",
        ClientManifests.StartRunDocument
    );

    private IReadOnlyList<ClientFile> RenderDocuments(
        ClientManifest manifest,
        params ClientDocumentFile[] documents
    ) =>
        new ClientGenerator(new FakeNodeRunner()).Render(
            new ClientGenerateOptions(manifest, ToolsDir: _tools, Documents: documents)
        );

    [Test]
    public void Every_generated_module_checks_the_client_format_before_anything_else()
    {
        var files = Render();

        Text(files, "format.g.ts")
            .Should()
            .Contain($"requireClientFormat(generatedClientFormat)")
            .And.Contain($"generatedClientFormat = {ClientGenerator.ClientFormat}");
        foreach (
            var file in files.Where(f =>
                f.Path.EndsWith(".g.ts") && f.Path is not "format.g.ts" and not "types.g.ts"
            )
        )
        {
            if (file.Path.EndsWith(".machine.g.ts") || file.Path.EndsWith(".contexts.g.ts"))
                continue;
            FirstImport(file.Text)
                .Should()
                .Match("*format.g\";", $"{file.Path} loads the format check first");
        }
    }

    [Test]
    public void A_machine_is_sent_by_the_ids_of_its_documents()
    {
        var text = Text(Render(), "checkout/checkout.client.g.ts");

        text.Should().Contain("export const checkoutDocuments: readonly MachineDocument[]");
        text.Should().Contain("operation: \"changed\"").And.Contain("operation: \"get\"");
        text.Should()
            .Contain("export function useCheckout(")
            .And.Contain("export function useRememberedCheckout(");
        text.Should()
            .Contain(
                "const { user } = traxConnection();",
                "the user is the connection's, so it cannot disagree with the headers for a render"
            )
            .And.Contain("give configureTrax the signed-in user's id as `user`")
            .And.NotContain("user: string")
            .And.Contain(
                "\"trax-start:checkout\" + (user === undefined ? \"\" : \".\" + encodeURIComponent(user))"
            )
            .And.Contain(
                "const key = (rememberKey ?? \"trax.checkout\") + \".\" + encodeURIComponent(user);",
                "each user's instance is kept under a key of its own"
            );
    }

    [Test]
    public void A_train_gets_a_function_only_from_a_document_the_frontend_wrote()
    {
        Render()
            .Should()
            .NotContain(
                f => f.Path.StartsWith("trains/", StringComparison.Ordinal),
                "the host generates no document for a train, per Trax.Docs/adr/0054"
            );

        var files = RenderDocuments(ClientManifests.Sample(), StartRunFile);
        var train = Text(files, "documents/startRun.g.ts");
        var types = Text(files, "types.g.ts");

        train
            .Should()
            .Contain(
                "export function startRun(input: StartRunInput, options: { mode?: ExecutionMode | null; priority?: number | null } = {}): Promise<StartRunResult>"
            )
            .And.Contain("[\"dispatch\", \"startRun\"]")
            .And.Contain("holding one of the roles recovery-user")
            .And.Contain(
                """
                export type StartRunResult = {
                  externalId: string;
                  metadataId: number | null;
                  workQueueId: number | null;
                  output: {
                    runId: string;
                    steps: Array<string>;
                  } | null;
                };
                """
            );
        types.Should().Contain("export type ExecutionMode = \"RUN\" | \"QUEUE\";");
        types.Should().Contain("  scenario: string;").And.Contain("  crashOnce?: boolean | null;");
        types.Should().NotContain("DispatchMutations", "a type that holds operations is not data");
        Text(files, "index.g.ts")
            .Should()
            .Contain("export { startRun, type StartRunResult } from \"./documents/startRun.g\";");
    }

    [Test]
    public void A_type_named_like_an_operation_holder_is_still_data()
    {
        var sample = ClientManifests.Sample();
        var manifest = sample with
        {
            Types = sample.Types + "\n\ninput OrderDispatchMutations {\n  id: String!\n}\n",
        };

        Text(Render(manifest), "types.g.ts")
            .Should()
            .Contain(
                "export interface OrderDispatchMutations",
                "the host leaves out the types that hold operations, so every type it lists is data"
            );
    }

    [Test]
    public void A_train_without_its_answer_is_refused()
    {
        var sample = ClientManifests.Sample();
        var manifest = sample with { Trains = [sample.Trains[0] with { Answer = null }] };

        var render = () => RenderDocuments(manifest, StartRunFile);

        render.Should().Throw<InvalidOperationException>().WithMessage("*startRun*answers with*");
    }

    [TestCase("!!")]
    [TestCase("Int } type X { y: Int")]
    public void A_train_answer_that_is_not_a_graphql_type_is_refused_with_a_sentence(string answer)
    {
        var sample = ClientManifests.Sample();
        var manifest = sample with { Trains = [sample.Trains[0] with { Answer = answer }] };

        var render = () => RenderDocuments(manifest, StartRunFile);

        render
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*startRun*not a GraphQL type*Export*");
    }

    [TestCase("server")]
    [TestCase("socket")]
    [TestCase("trax-socket")]
    [TestCase("use-server-machine")]
    [TestCase("ir")]
    [TestCase("queue-microtask")]
    public void A_machine_named_as_a_name_its_client_module_uses_is_refused(string id)
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Id = id }] };

        var render = () => Render(manifest, node);

        render
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{id}'*its generated modules use*");
        node.Calls.Should().BeEmpty();
    }

    [TestCase("states", "__proto__")]
    [TestCase("states", "constructor")]
    [TestCase("triggers", "toString")]
    public void A_state_named___proto___is_refused(string list, string name)
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        ((JsonArray)ir[list]!).Add(name);
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = () => Render(manifest, node);

        render
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*\"{name}\"*every JavaScript object has*");
        node.Calls.Should().BeEmpty();
    }

    [TestCase("context", "__proto__", "states")]
    [TestCase("context", "Nowhere", "states")]
    [TestCase("invariants", "__proto__", "states")]
    [TestCase("inputs", "__proto__", "triggers")]
    [TestCase("inputs", "Cart", "triggers")]
    public void An_ir_with_a_context_key_that_is_not_a_state_is_refused(
        string map,
        string key,
        string list
    )
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        if (ir[map] is not JsonObject entries)
            ir[map] = entries = new JsonObject();
        entries[key] = new JsonObject { ["fields"] = new JsonArray() };
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = () => Render(manifest, node);

        render
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{map} for \"{key}\", which is not one of its {list}*");
        node.Calls.Should().BeEmpty();
    }

    [Test]
    public void An_ir_with_a___proto___key_anywhere_is_refused()
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        ir["initialContext"] = new JsonObject { ["__proto__"] = new JsonObject { ["x"] = 1 } };
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = () => Render(manifest, node);

        render.Should().Throw<InvalidOperationException>().WithMessage("*\"__proto__\"*prototype*");
        node.Calls.Should().BeEmpty();
    }

    [Test]
    public void A_machine_server_is_made_again_after_the_connection_changes()
    {
        var files = Render();

        Text(files, "client.g.ts")
            .Should()
            .Contain("generation += 1;")
            .And.Contain("export function traxGeneration(): number");
        Text(files, "checkout/checkout.client.g.ts")
            .Should()
            .Contain(
                "if (server === null || serverGeneration !== generation)",
                "a server made before configureTrax was called again holds the socket it closed"
            )
            .And.Contain(
                "if (generation !== traxGeneration()) throw changed();",
                "a server made before configureTrax was called again sends nothing"
            )
            .And.Contain("return connection.headers?.() ?? {};")
            .And.NotContain("traxConnection().headers")
            .And.Contain("useSyncExternalStore(subscribeTrax, traxGeneration, traxGeneration);")
            .And.NotContain("server ??=");
    }

    [Test]
    public void A_host_whose_trains_can_be_followed_gets_a_progress_module()
    {
        var files = Render();
        var progress = Text(files, "progress.g.ts");

        FirstImport(progress).Should().Match("*format.g\";");
        progress
            .Should()
            .Contain("export const runProgressDocumentId = \"trax_progress_query_")
            .And.Contain("export const runProgressEventsDocumentId = \"trax_progress_subscription_")
            .And.Contain("export function readRunProgress(runId: string): Promise<RunProgress>")
            .And.Contain("export function followRunProgress(")
            .And.Contain("export function useRunProgress(")
            .And.Contain("if (event.retriedAs) return retry(event.retriedAs);")
            .And.Contain("Runs of these trains can be followed: startRun.")
            .And.NotContain(
                ClientManifests.ProgressQuery,
                "a document is sent by its id, never its text"
            );
        Text(files, "index.g.ts")
            .Should()
            .Contain("useRunProgress")
            .And.Contain("type RunProgressView");
    }

    [Test]
    public void A_host_without_one_gets_none()
    {
        var files = Render(ClientManifests.Sample() with { Progress = [] });

        files.Should().NotContain(f => f.Path == "progress.g.ts");
        Text(files, "index.g.ts").Should().NotContain("RunProgress");
    }

    [Test]
    public void A_progress_section_that_is_not_one_query_and_one_subscription_is_refused()
    {
        var manifest = ClientManifests.Sample() with
        {
            Progress = [ClientManifests.Sample().Progress[0]],
        };

        var render = () => Render(manifest);

        render.Should().Throw<InvalidOperationException>().WithMessage("*progress documents*");
    }

    [Test]
    public void A_description_cannot_close_its_comment()
    {
        var types = Text(Render(), "types.g.ts");

        types.Should().Contain("*\\/ alert('x') /*").And.NotContain("*/ alert");
    }

    [Test]
    public void A_machine_id_that_is_a_reserved_word_is_refused()
    {
        var manifest = ClientManifests.Sample() with
        {
            Machines = [ClientManifests.Sample().Machines[0] with { Id = "delete" }],
        };

        var render = () => Render(manifest);

        render.Should().Throw<InvalidOperationException>().WithMessage("*'delete'*reserves*");
    }

    [Test]
    public void Two_machines_that_generate_one_name_are_refused()
    {
        var first = ClientManifests.Sample().Machines[0];
        var manifest = ClientManifests.Sample() with
        {
            Machines = [first with { Id = "map-2d" }, first with { Id = "map2d" }],
        };

        var render = () => Render(manifest);

        render.Should().Throw<InvalidOperationException>().WithMessage("*map-2d*map2d*");
    }

    [Test]
    public void The_same_manifest_writes_the_same_bytes()
    {
        Render().Select(f => f.Text).Should().Equal(Render().Select(f => f.Text));
    }

    [Test]
    public void Check_reports_a_changed_file_and_a_stale_one_and_ignores_line_endings()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(
            ClientManifests.Sample(),
            ToolsDir: _tools,
            Documents: [StartRunFile]
        );
        generator.Write(options, _out);
        generator.Check(options, _out).Should().BeEmpty();

        var client = Path.Combine(_out, "client.g.ts");
        File.WriteAllText(client, File.ReadAllText(client).Replace("\n", "\r\n"));
        generator.Check(options, _out).Should().BeEmpty("a checkout that writes CRLF is not drift");

        File.AppendAllText(Path.Combine(_out, "documents", "startRun.g.ts"), "// edited\n");
        File.WriteAllText(Path.Combine(_out, "documents", "gone.g.ts"), GeneratedText);
        generator
            .Check(options, _out)
            .Select(d => (d.Path, d.Status))
            .Should()
            .BeEquivalentTo([
                ("documents/startRun.g.ts", DriftStatus.Drifted),
                ("documents/gone.g.ts", DriftStatus.Drifted),
            ]);
    }

    [Test]
    public void An_ir_whose_id_differs_from_the_machine_is_refused()
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        ir["id"] = "../escaped";
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = () => Render(manifest, node);

        render.Should().Throw<InvalidOperationException>().WithMessage("*'checkout'*'../escaped'*");
        node.Calls.Should().BeEmpty("the IR is refused before node reads it");
    }

    [Test]
    public void A_state_name_that_is_not_an_identifier_is_refused()
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        ((JsonArray)ir["states"]!).Add("Done\"; alert(1); \"");
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = () => Render(manifest, node);

        render.Should().Throw<InvalidOperationException>().WithMessage("*state*not an identifier*");
        node.Calls.Should().BeEmpty();
    }

    [Test]
    public void An_outcome_that_is_not_a_state_and_a_kind_is_refused()
    {
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        ir["outcomes"] = new JsonObject { ["Fetching.done;x"] = new JsonObject() };
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = () => Render(manifest);

        render
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*outcome*not an identifier*");
    }

    [Test]
    public void An_ir_hash_that_is_not_the_irs_is_refused()
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var manifest = ClientManifests.Sample() with
        {
            Machines = [checkout with { IrHash = new string('0', 64) }],
        };

        var render = () => Render(manifest, node);

        render.Should().Throw<InvalidOperationException>().WithMessage("*hashes to*");
        node.Calls.Should().BeEmpty();
    }

    [Test]
    public void A_train_field_that_is_not_a_graphql_name_is_refused()
    {
        var train = ClientManifests.Sample().Trains[0];
        var manifest = ClientManifests.Sample() with
        {
            Trains = [train with { Field = "startRun\n" }],
        };

        var render = () => Render(manifest);

        render.Should().Throw<InvalidOperationException>().WithMessage("*not a GraphQL name*");
        TypeScript.IsIdentifier("startRun\n").Should().BeFalse();
    }

    [Test]
    public void Write_removes_a_generated_file_the_manifest_no_longer_generates()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        generator.Write(options, _out);
        // What a version that generated a function per train left behind.
        Directory.CreateDirectory(Path.Combine(_out, "trains"));
        File.WriteAllText(Path.Combine(_out, "trains", "startRun.g.ts"), GeneratedText);
        File.WriteAllText(Path.Combine(_out, "notes.ts"), "// the app's own\n");
        Directory.CreateDirectory(Path.Combine(_out, "old"));
        File.WriteAllText(Path.Combine(_out, "old", "old.client.g.ts"), GeneratedText);
        File.WriteAllText(
            Path.Combine(_out, "old", "old.machine.g.ts"),
            "// AUTO-GENERATED from old.ir.json by generateMachineFactory. Do not edit by hand.\n"
        );

        var trainless = ClientManifests.Sample() with { Trains = [], Progress = [] };
        generator.Write(options with { Manifest = trainless }, _out);

        File.Exists(Path.Combine(_out, "trains", "startRun.g.ts")).Should().BeFalse();
        Directory
            .Exists(Path.Combine(_out, "trains"))
            .Should()
            .BeFalse("a folder left empty goes too");
        Directory.Exists(Path.Combine(_out, "old")).Should().BeFalse();
        File.Exists(Path.Combine(_out, "notes.ts"))
            .Should()
            .BeTrue("only generated files are removed");
        generator.Check(options with { Manifest = trainless }, _out).Should().BeEmpty();
    }

    private const string GeneratedText =
        "// Generated by trax client from the host's client manifest. Do not edit.\n";

    [Test]
    public void Write_leaves_a_g_ts_file_it_did_not_generate()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        Directory.CreateDirectory(Path.Combine(_out, "app"));
        var own = Path.Combine(_out, "app", "routes.g.ts");
        File.WriteAllText(own, "// Generated by the app's router. Do not edit.\n");
        var empty = Path.Combine(_out, "empty.g.ts");
        File.WriteAllText(empty, "");

        generator.Write(options, _out);

        File.Exists(own).Should().BeTrue("another generator's file under --out is not this one's");
        File.Exists(empty).Should().BeTrue();
        generator.Check(options, _out).Should().BeEmpty("only this generator's files are stale");
    }

    [Test]
    public void Write_does_not_follow_a_symlinked_folder()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        var shared = Path.Combine(_tools, "shared");
        Directory.CreateDirectory(shared);
        var outside = Path.Combine(shared, "outside.g.ts");
        File.WriteAllText(outside, GeneratedText);
        Directory.CreateDirectory(_out);
        Directory.CreateSymbolicLink(Path.Combine(_out, "shared"), shared);

        generator.Write(options, _out);

        File.Exists(outside).Should().BeTrue("a linked folder leads outside --out");
        generator.Check(options, _out).Should().BeEmpty();
    }

    [Test]
    public void Write_does_not_write_through_a_symlinked_machine_folder()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        var outside = Path.Combine(_tools, "elsewhere");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(_out);
        Directory.CreateSymbolicLink(Path.Combine(_out, "checkout"), outside);

        var write = () => generator.Write(options, _out);

        write.Should().Throw<InvalidOperationException>().WithMessage("*checkout is a link*");
        Directory
            .EnumerateFileSystemEntries(outside)
            .Should()
            .BeEmpty("nothing is written through the link");
        File.Exists(Path.Combine(_out, "format.g.ts"))
            .Should()
            .BeFalse("the link is refused before anything is written");
        var check = () => generator.Check(options, _out);
        check.Should().Throw<InvalidOperationException>().WithMessage("*checkout is a link*");
    }

    [Test]
    public void Write_does_not_write_through_a_symlinked_file()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        var outside = Path.Combine(_tools, "outside.ts");
        File.WriteAllText(outside, "// the app's own\n");
        Directory.CreateDirectory(_out);
        File.CreateSymbolicLink(Path.Combine(_out, "format.g.ts"), outside);

        var write = () => generator.Write(options, _out);

        write.Should().Throw<InvalidOperationException>().WithMessage("*format.g.ts is a link*");
        File.ReadAllText(outside).Should().Be("// the app's own\n");
    }

    [Test]
    public void Write_skips_a_file_that_is_not_regular()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("A FIFO is made with mkfifo.");
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        Directory.CreateDirectory(_out);
        var fifo = Path.Combine(_out, "x.g.ts");
        MakeFifo(fifo);
        MakeFifo(Path.Combine(_out, "format.g.ts"));

        // Opening a FIFO blocks until something writes to it, so a run that opens one never ends.
        var run = Task.Run(() =>
        {
            var drift = generator.Check(options, _out);
            generator.Write(options, _out);
            return drift;
        });

        run.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("a FIFO under --out is never opened");
        run.Result.Should().Contain(new ClientDrift("format.g.ts", DriftStatus.Drifted));
        run.Result.Select(d => d.Path).Should().NotContain("x.g.ts");
        File.Exists(fifo).Should().BeTrue("a file that is not regular is not this generator's");
        File.ReadAllText(Path.Combine(_out, "format.g.ts")).Should().StartWith(GeneratedText);
        generator.Check(options, _out).Should().BeEmpty();
    }

    private static void MakeFifo(string path)
    {
        using var mkfifo = System.Diagnostics.Process.Start("mkfifo", [path]);
        mkfifo.WaitForExit();
        mkfifo.ExitCode.Should().Be(0);
    }

    private static string TwinHeader(string id, string kind) =>
        $"// AUTO-GENERATED from {id}.ir.json by {(kind == "machine" ? "generateMachineFactory" : "generateContextTypes")}. Do not edit by hand.\n";

    [Test]
    public void Write_leaves_a_twin_trax_machine_generate_wrote_for_a_machine_the_manifest_does_not_serve()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        generator.Write(options, _out);
        Directory.CreateDirectory(Path.Combine(_out, "reminder"));
        var machine = Path.Combine(_out, "reminder", "reminder.machine.g.ts");
        var contexts = Path.Combine(_out, "reminder", "reminder.contexts.g.ts");
        File.WriteAllText(machine, TwinHeader("reminder", "machine"));
        File.WriteAllText(contexts, TwinHeader("reminder", "contexts"));
        var other = Path.Combine(_out, "schema.g.ts");
        File.WriteAllText(other, "// AUTO-GENERATED from schema.graphql by another tool.\n");

        generator.Write(options, _out);

        File.Exists(machine)
            .Should()
            .BeTrue(
                "the previous run did not serve reminder, so trax client did not write its twin"
            );
        File.Exists(contexts).Should().BeTrue();
        File.Exists(other).Should().BeTrue("a header that only starts like a twin's is not one");
        generator.Check(options, _out).Should().BeEmpty();
    }

    [Test]
    public void Write_removes_the_twin_of_a_machine_the_previous_run_served()
    {
        var node = new FakeNodeRunner
        {
            MachineContent = TwinHeader("checkout", "machine"),
            ContextsContent = TwinHeader("checkout", "contexts"),
        };
        var generator = new ClientGenerator(node);
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        generator.Write(options, _out);
        // The client module goes first, so only the manifest.json the previous run wrote says checkout was served.
        File.Delete(Path.Combine(_out, "checkout", "checkout.client.g.ts"));

        var machineless = options with
        {
            Manifest = ClientManifests.Sample() with { Machines = [] },
        };
        generator
            .Check(machineless, _out)
            .Select(d => d.Path)
            .Should()
            .Contain(["checkout/checkout.machine.g.ts", "checkout/checkout.contexts.g.ts"]);
        generator.Write(machineless, _out);

        Directory.Exists(Path.Combine(_out, "checkout")).Should().BeFalse();
        generator.Check(machineless, _out).Should().BeEmpty();
    }

    [TestCase("configureTrax")]
    [TestCase("TraxOperationError")]
    public void A_type_that_takes_a_runtime_name_is_refused(string name)
    {
        var typeManifest = ClientManifests.Sample() with
        {
            Types = ClientManifests.TrainTypes + $"\nenum {name} {{ X }}\n",
        };

        ((Action)(() => Render(typeManifest)))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*runtime*{name}*");
    }

    [TestCase("options")]
    [TestCase("sendDocument")]
    [TestCase("startRunDocumentId")]
    public void A_variable_named_as_a_local_of_the_function_is_refused(string name)
    {
        var document = StartRunFile with
        {
            Text = ClientManifests.StartRunDocument.Replace("$input", "$" + name),
        };

        var render = () => RenderDocuments(ClientManifests.Sample(), document);

        render.Should().Throw<InvalidOperationException>().WithMessage($"*'{name}'*");
    }

    [Test]
    public void A_reserved_word_the_runtime_forbids_is_refused()
    {
        foreach (var word in new[] { "Eval", "Arguments" })
        {
            var document = StartRunFile with
            {
                Text = ClientManifests.StartRunDocument.Replace(
                    "mutation StartRun",
                    $"mutation {word}"
                ),
            };
            var render = () => RenderDocuments(ClientManifests.Sample(), document);
            render
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage($"*'{word.ToLowerInvariant()}'*reserves*");
        }
    }

    [Test]
    public void A_progress_module_with_no_followable_train_names_none()
    {
        var train = ClientManifests.Sample().Trains[0] with { Progress = false };

        var progress = Text(
            Render(ClientManifests.Sample() with { Trains = [train] }),
            "progress.g.ts"
        );

        progress.Should().NotContain("can be followed");
    }

    [Test]
    public void The_generator_and_the_runtime_name_one_client_format()
    {
        var source = Path.Combine(EngineSrc(), "clientFormat.ts");
        var match = ClientFormatDeclaration().Match(File.ReadAllText(source));

        match
            .Success.Should()
            .BeTrue("clientFormat.ts declares `export const clientFormat = <n>;`");
        int.Parse(match.Groups["format"].Value)
            .Should()
            .Be(
                ClientGenerator.ClientFormat,
                "the generator writes the format the runtime serves; bump both together"
            );
    }

    private static string FirstImport(string text) =>
        text.Split('\n').First(l => l.StartsWith("import ", StringComparison.Ordinal));

    private static string EngineSrc()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            var candidate = Path.Combine(dir.FullName, "Trax.Api.StateMachine", "src");
            if (Directory.Exists(candidate))
                return candidate;
        }
        Assert.Ignore("Trax.Api.StateMachine not found (isolated build).");
        return "";
    }

    [GeneratedRegex(@"^export const clientFormat = (?<format>\d+);$", RegexOptions.Multiline)]
    private static partial Regex ClientFormatDeclaration();
}
