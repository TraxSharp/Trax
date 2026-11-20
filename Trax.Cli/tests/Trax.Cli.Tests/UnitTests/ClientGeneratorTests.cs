using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Cli.Clients;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;
using Trax.Core.Extensions;
using Trax.StateMachine;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax client</c> renders a frontend's client from a host's manifest: every module checks the client format
/// before anything else, a machine's documents are sent by id, a train gets a function only from a document the
/// frontend wrote, its input and answer typed from the manifest's SDL, anything from the manifest is written as data (names checked, strings as literals,
/// comments unable to close), the same manifest writes the same bytes, and <c>--check</c> reports what differs.
/// </summary>
public partial class ClientGeneratorTests
{
    private string _tools = "";
    private string _out = "";

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

    private async Task<IReadOnlyList<ClientFile>> RenderAsync(
        ClientManifest? manifest = null,
        FakeNodeRunner? node = null
    ) =>
        (
            await new ClientGenerator(node ?? new FakeNodeRunner())
                .RenderAsync(
                    new ClientGenerateOptions(
                        manifest ?? ClientManifests.Sample(),
                        ToolsDir: _tools
                    )
                )
                .ConfigureAwait(false)
        );

    private static string Text(IReadOnlyList<ClientFile> files, string path) =>
        files.Single(f => string.Equals(f.Path, path, StringComparison.Ordinal)).Text;

    private static readonly ClientDocumentFile StartRunFile = new(
        "src/startRun.graphql",
        ClientManifests.StartRunDocument
    );

    private async Task<IReadOnlyList<ClientFile>> RenderDocumentsAsync(
        ClientManifest manifest,
        params ClientDocumentFile[] documents
    ) =>
        (
            await new ClientGenerator(new FakeNodeRunner())
                .RenderAsync(
                    new ClientGenerateOptions(manifest, ToolsDir: _tools, Documents: documents)
                )
                .ConfigureAwait(false)
        );

    [Test]
    public async Task Every_generated_module_checks_the_client_format_before_anything_else()
    {
        var files = await RenderAsync().ConfigureAwait(false);

        Text(files, "format.g.ts")
            .Should()
            .Contain($"requireClientFormat(generatedClientFormat)")
            .And.Contain($"generatedClientFormat = {ClientGenerator.ClientFormat}");
        foreach (
            var file in files.Where(f =>
                f.Path.EndsWith(".g.ts", StringComparison.Ordinal)
                && f.Path is not "format.g.ts" and not "types.g.ts"
            )
        )
        {
            if (
                file.Path.EndsWith(".machine.g.ts", StringComparison.Ordinal)
                || file.Path.EndsWith(".contexts.g.ts", StringComparison.Ordinal)
            )
                continue;
            FirstImport(file.Text)
                .Should()
                .Match("*format.g\";", $"{file.Path} loads the format check first");
        }
    }

    [Test]
    public async Task A_machine_is_sent_by_the_ids_of_its_documents()
    {
        var text = Text(await RenderAsync().ConfigureAwait(false), "checkout/checkout.client.g.ts");

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
    public async Task A_train_gets_a_function_only_from_a_document_the_frontend_wrote()
    {
        (await RenderAsync().ConfigureAwait(false))
            .Should()
            .NotContain(
                f => f.Path.StartsWith("trains/", StringComparison.Ordinal),
                "the host generates no document for a train, per Trax.Docs/adr/0054"
            );

        var files = await RenderDocumentsAsync(ClientManifests.Sample(), StartRunFile)
            .ConfigureAwait(false);
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
    public async Task A_type_named_like_an_operation_holder_is_still_data()
    {
        var sample = ClientManifests.Sample();
        var manifest = sample with
        {
            Types = sample.Types + "\n\ninput OrderDispatchMutations {\n  id: String!\n}\n",
        };

        Text(await RenderAsync(manifest).ConfigureAwait(false), "types.g.ts")
            .Should()
            .Contain(
                "export interface OrderDispatchMutations",
                "the host leaves out the types that hold operations, so every type it lists is data"
            );
    }

    [Test]
    public async Task A_train_without_its_answer_is_refused()
    {
        var sample = ClientManifests.Sample();
        var manifest = sample with { Trains = [sample.Trains[0] with { Answer = null }] };

        var render = async () =>
            await RenderDocumentsAsync(manifest, StartRunFile).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*startRun*answers with*");
    }

    [TestCase("!!")]
    [TestCase("Int } type X { y: Int")]
    public async Task A_train_answer_that_is_not_a_graphql_type_is_refused_with_a_sentence(
        string answer
    )
    {
        var sample = ClientManifests.Sample();
        var manifest = sample with { Trains = [sample.Trains[0] with { Answer = answer }] };

        var render = async () =>
            await RenderDocumentsAsync(manifest, StartRunFile).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*startRun*not a GraphQL type*Export*");
    }

    [TestCase("server")]
    [TestCase("socket")]
    [TestCase("trax-socket")]
    [TestCase("use-server-machine")]
    [TestCase("ir")]
    [TestCase("ir-hash")]
    [TestCase("shared-output0")]
    [TestCase("queue-microtask")]
    public async Task A_machine_named_as_a_name_its_client_module_uses_is_refused(string id)
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Id = id }] };

        var render = async () => await RenderAsync(manifest, node).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage($"*'{id}'*its generated modules use*");
        node.Calls.Should().BeEmpty();
    }

    [TestCase("machine", "useRememberedMachine")]
    [TestCase("server-machine", "useServerMachine")]
    [TestCase("create-machine", "createMachineServer")]
    public async Task A_machine_whose_functions_are_named_as_a_name_its_client_module_uses_is_refused(
        string id,
        string function
    )
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Id = id }] };

        var render = async () => await RenderAsync(manifest, node).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage($"*'{id}'*'{function}'*its generated modules use*");
        node.Calls.Should().BeEmpty();
    }

    [TestCase("states", "__proto__")]
    [TestCase("states", "constructor")]
    [TestCase("triggers", "toString")]
    public async Task A_state_named___proto___is_refused(string list, string name)
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        ((JsonArray)ir[list].NotNull()).Add(name);
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = async () => await RenderAsync(manifest, node).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage($"*\"{name}\"*every JavaScript object has*");
        node.Calls.Should().BeEmpty();
    }

    [TestCase("context", "__proto__", "states")]
    [TestCase("context", "Nowhere", "states")]
    [TestCase("invariants", "__proto__", "states")]
    [TestCase("inputs", "__proto__", "triggers")]
    [TestCase("inputs", "Cart", "triggers")]
    public async Task An_ir_with_a_context_key_that_is_not_a_state_is_refused(
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

        var render = async () => await RenderAsync(manifest, node).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage($"*{map} for \"{key}\", which is not one of its {list}*");
        node.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task An_ir_with_a___proto___key_anywhere_is_refused()
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        ir["initialContext"] = new JsonObject { ["__proto__"] = new JsonObject { ["x"] = 1 } };
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = async () => await RenderAsync(manifest, node).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*\"__proto__\"*prototype*");
        node.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task A_machine_server_is_made_again_after_the_connection_changes()
    {
        var files = await RenderAsync().ConfigureAwait(false);

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
    public async Task A_machine_server_takes_the_connections_consent_to_cleartext()
    {
        var files = await RenderAsync().ConfigureAwait(false);

        Text(files, "client.g.ts")
            .Should()
            .Contain("allowInsecureTransport?: boolean;")
            .And.Contain(
                "requireSecureEndpoint(url, allowInsecureTransport);",
                "a train function refuses a cleartext url to another host as a machine's server does"
            );
        Text(files, "checkout/checkout.client.g.ts")
            .Should()
            .Contain(
                "allowInsecureTransport: connection.allowInsecureTransport,",
                "a machine's server refuses a cleartext url to another host unless the app said the network is trusted"
            )
            .And.Contain(
                "maxResponseSize: connection.maxResponseBytes,",
                "a machine's server reads no more of an answer than the connection allows"
            );
    }

    [Test]
    public async Task The_socket_is_held_to_TLS_and_an_answer_is_capped()
    {
        var files = await RenderAsync().ConfigureAwait(false);
        var client = Text(files, "client.g.ts");

        client
            .Should()
            .Contain(
                "requireSecureSocket(socketUrl, allowInsecureTransport);",
                "the socket carries the credential in connection_init, so a ws: socket to another host is refused"
            )
            .And.Contain("export function requireSecureSocket(")
            .And.Contain(
                "shouldRetry: retryClosedSocket,",
                "graphql-ws retries a 4403 by default, so the stream would never see the host turn the caller away"
            )
            .And.Contain("return typeof code === \"number\" && code !== 4401 && code !== 4403;")
            .And.Contain("redirect: \"error\",")
            .And.Contain(
                "requireNoRedirect(response);",
                "a fetch that ignores redirect: \"error\" must not have the answer it followed to read"
            )
            .And.Contain(
                "if (!response.ok)\n    throw new TraxOperationError(undefined,",
                "an answer that is not 2xx and names no GraphQL error is a failed request, not undefined data"
            )
            .And.Contain(
                "if (typeof data !== \"object\" || data === null)\n    throw new TraxOperationError(undefined, `The host answered ${response.status} with no GraphQL answer.`);",
                "a 2xx answer with no data to read ({}, null, a number, data: null) is refused, not undefined data"
            )
            .And.Contain("maxResponseBytes?: number;")
            .And.Contain("await readJson(response, maxResponseBytes)")
            .And.NotContain("response.json()");
        client
            .IndexOf("requireSecureSocket(socketUrl", StringComparison.Ordinal)
            .Should()
            .BeLessThan(
                client.IndexOf("createClient({", StringComparison.Ordinal),
                "the socket is checked before it is made"
            );
        Text(files, "index.g.ts")
            .Should()
            .Contain("export { configureTrax, requireSecureSocket, TraxOperationError");
    }

    [Test]
    public async Task A_host_whose_trains_can_be_followed_gets_a_progress_module()
    {
        var files = await RenderAsync().ConfigureAwait(false);
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
    public async Task A_host_serving_views_gets_a_read_function_per_view_sent_by_its_id()
    {
        const string text = "query OrdersView { views { orders { vOrder { id status } } } }";
        var manifest = ClientManifests.Sample() with
        {
            Views =
            [
                new ClientManifestDocument
                {
                    Id = "trax_view_orders_" + new string('a', 64) + new string('b', 16),
                    Operation = "orders",
                    Document = text,
                },
            ],
            Types =
                ClientManifests.Sample().Types
                + """

                    type OrdersView {
                      vOrder: [OrdersVOrderRow!]!
                    }

                    type OrdersVOrderRow {
                      id: UUID!
                      status: String
                    }

                    scalar UUID
                    """,
        };

        var files = await RenderAsync(manifest).ConfigureAwait(false);
        var views = Text(files, "views.g.ts");

        FirstImport(views).Should().Match("*format.g\";");
        views
            .Should()
            .Contain("export type OrdersViewResult = ")
            .And.Contain("export const ordersViewDocumentId = \"trax_view_orders_")
            .And.Contain("export function readOrdersView(): Promise<OrdersViewResult>")
            .And.Contain("sendDocument(ordersViewDocumentId, {}, [\"views\", \"orders\"])")
            .And.NotContain(text, "a document is sent by its id, never its text");
        Text(files, "index.g.ts")
            .Should()
            .Contain(
                "export { readOrdersView, type OrdersViewResult, ordersViewDocumentId } from \"./views.g\";"
            );
    }

    // The view's name is only ever written inside longer names (readNewView) or as a string, so a word TypeScript
    // reserves names a view as well as any other.
    [TestCase("new", "New")]
    [TestCase("default", "Default")]
    [TestCase("type", "Type")]
    [TestCase("delete", "Delete")]
    public async Task A_view_named_for_a_reserved_word_is_generated(string operation, string pascal)
    {
        var text = $"query {pascal}View {{ views {{ {operation} {{ vOrder {{ id status }} }} }} }}";
        var manifest = ClientManifests.Sample() with
        {
            Views =
            [
                new ClientManifestDocument
                {
                    Id = $"trax_view_{operation}_" + new string('a', 64) + new string('b', 16),
                    Operation = operation,
                    Document = text,
                },
            ],
            Types =
                ClientManifests.Sample().Types
                + $$"""

                    type {{pascal}}View {
                      vOrder: [{{pascal}}VOrderRow!]!
                    }

                    type {{pascal}}VOrderRow {
                      id: UUID!
                      status: String
                    }

                    scalar UUID
                    """,
        };

        var views = Text(await RenderAsync(manifest).ConfigureAwait(false), "views.g.ts");

        views
            .Should()
            .Contain($"export type {pascal}ViewResult = ")
            .And.Contain($"export const {operation}ViewDocumentId = \"trax_view_{operation}_")
            .And.Contain($"export function read{pascal}View(): Promise<{pascal}ViewResult>")
            .And.Contain($"[\"views\", \"{operation}\"]");
    }

    [Test]
    public async Task A_host_without_one_gets_none()
    {
        var files = await RenderAsync(ClientManifests.Sample() with { Progress = [] })
            .ConfigureAwait(false);

        files.Should().NotContain(f => f.Path == "progress.g.ts");
        Text(files, "index.g.ts").Should().NotContain("RunProgress");
    }

    [Test]
    public async Task A_progress_section_that_is_not_one_query_and_one_subscription_is_refused()
    {
        var manifest = ClientManifests.Sample() with
        {
            Progress = [ClientManifests.Sample().Progress[0]],
        };

        var render = async () => await RenderAsync(manifest).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*progress documents*");
    }

    [Test]
    public async Task A_description_cannot_close_its_comment()
    {
        var types = Text(await RenderAsync().ConfigureAwait(false), "types.g.ts");

        types.Should().Contain("*\\/ alert('x') /*").And.NotContain("*/ alert");
    }

    [Test]
    public async Task A_machine_id_that_is_a_reserved_word_is_refused()
    {
        var manifest = ClientManifests.Sample() with
        {
            Machines = [ClientManifests.Sample().Machines[0] with { Id = "delete" }],
        };

        var render = async () => await RenderAsync(manifest).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*'delete'*reserves*");
    }

    [Test]
    public async Task Two_machines_that_generate_one_name_are_refused()
    {
        var first = ClientManifests.Sample().Machines[0];
        var manifest = ClientManifests.Sample() with
        {
            Machines = [first with { Id = "map-2d" }, first with { Id = "map2d" }],
        };

        var render = async () => await RenderAsync(manifest).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*map-2d*map2d*");
    }

    [Test]
    public async Task The_same_manifest_writes_the_same_bytes()
    {
        (await RenderAsync().ConfigureAwait(false))
            .Select(f => f.Text)
            .Should()
            .Equal((await RenderAsync().ConfigureAwait(false)).Select(f => f.Text));
    }

    [Test]
    public async Task Check_reports_a_changed_file_and_a_stale_one_and_ignores_line_endings()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(
            ClientManifests.Sample(),
            ToolsDir: _tools,
            Documents: [StartRunFile]
        );
        await generator.WriteAsync(options, _out).ConfigureAwait(false);
        (await generator.CheckAsync(options, _out).ConfigureAwait(false)).Should().BeEmpty();

        var client = Path.Combine(_out, "client.g.ts");
        await File.WriteAllTextAsync(
                client,
                (await File.ReadAllTextAsync(client).ConfigureAwait(false)).Replace(
                    "\n",
                    "\r\n",
                    StringComparison.Ordinal
                )
            )
            .ConfigureAwait(false);
        (await generator.CheckAsync(options, _out).ConfigureAwait(false))
            .Should()
            .BeEmpty("a checkout that writes CRLF is not drift");
        await File.AppendAllTextAsync(
                Path.Combine(_out, "documents", "startRun.g.ts"),
                "// edited\n"
            )
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(_out, "documents", "gone.g.ts"), GeneratedText)
            .ConfigureAwait(false);
        (await generator.CheckAsync(options, _out).ConfigureAwait(false))
            .Select(d => (d.Path, d.Status))
            .Should()
            .BeEquivalentTo([
                ("documents/startRun.g.ts", DriftStatus.Drifted),
                ("documents/gone.g.ts", DriftStatus.Drifted),
            ]);
    }

    [Test]
    public async Task An_ir_whose_id_differs_from_the_machine_is_refused()
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        ir["id"] = "../escaped";
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = async () => await RenderAsync(manifest, node).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*'checkout'*'../escaped'*");
        node.Calls.Should().BeEmpty("the IR is refused before node reads it");
    }

    [Test]
    public async Task A_state_name_that_is_not_an_identifier_is_refused()
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        ((JsonArray)ir["states"].NotNull()).Add("Done\"; alert(1); \"");
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = async () => await RenderAsync(manifest, node).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*state*not an identifier*");
        node.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task An_outcome_that_is_not_a_state_and_a_kind_is_refused()
    {
        var checkout = ClientManifests.Sample().Machines[0];
        var ir = (JsonObject)checkout.Ir.DeepClone();
        ir["outcomes"] = new JsonObject { ["Fetching.done;x"] = new JsonObject() };
        var manifest = ClientManifests.Sample() with { Machines = [checkout with { Ir = ir }] };

        var render = async () => await RenderAsync(manifest).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*outcome*not an identifier*");
    }

    [Test]
    public async Task An_ir_hash_that_is_not_the_irs_is_refused()
    {
        var node = new FakeNodeRunner();
        var checkout = ClientManifests.Sample().Machines[0];
        var manifest = ClientManifests.Sample() with
        {
            Machines = [checkout with { IrHash = new string('0', 64) }],
        };

        var render = async () => await RenderAsync(manifest, node).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*hashes to*");
        node.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task A_train_field_that_is_not_a_graphql_name_is_refused()
    {
        var train = ClientManifests.Sample().Trains[0];
        var manifest = ClientManifests.Sample() with
        {
            Trains = [train with { Field = "startRun\n" }],
        };

        var render = async () => await RenderAsync(manifest).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*not a GraphQL name*");
        TypeScript.IsIdentifier("startRun\n").Should().BeFalse();
    }

    [Test]
    public async Task Write_removes_a_generated_file_the_manifest_no_longer_generates()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        await generator.WriteAsync(options, _out).ConfigureAwait(false);
        // What a version that generated a function per train left behind.
        Directory.CreateDirectory(Path.Combine(_out, "trains"));
        await File.WriteAllTextAsync(Path.Combine(_out, "trains", "startRun.g.ts"), GeneratedText)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(_out, "notes.ts"), "// the app's own\n")
            .ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(_out, "old"));
        await File.WriteAllTextAsync(Path.Combine(_out, "old", "old.client.g.ts"), GeneratedText)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(_out, "old", "old.machine.g.ts"),
                "// AUTO-GENERATED from old.ir.json by generateMachineFactory. Do not edit by hand.\n"
            )
            .ConfigureAwait(false);

        var trainless = ClientManifests.Sample() with { Trains = [], Progress = [] };
        await generator
            .WriteAsync(options with { Manifest = trainless }, _out)
            .ConfigureAwait(false);

        File.Exists(Path.Combine(_out, "trains", "startRun.g.ts")).Should().BeFalse();
        Directory
            .Exists(Path.Combine(_out, "trains"))
            .Should()
            .BeFalse("a folder left empty goes too");
        Directory.Exists(Path.Combine(_out, "old")).Should().BeFalse();
        File.Exists(Path.Combine(_out, "notes.ts"))
            .Should()
            .BeTrue("only generated files are removed");
        (
            await generator
                .CheckAsync(options with { Manifest = trainless }, _out)
                .ConfigureAwait(false)
        )
            .Should()
            .BeEmpty();
    }

    private const string GeneratedText =
        "// Generated by trax client from the host's client manifest. Do not edit.\n";

    [TestCase(true)]
    [TestCase(false)]
    public async Task Two_clients_one_inside_the_other_leave_each_others_files_alone(
        bool outerFirst
    )
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var outer = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        var inner = outer with
        {
            Manifest = ClientManifests.Sample() with { Trains = [], Progress = [] },
        };
        var nested = Path.Combine(_out, "admin");

        for (var round = 0; round < 2; round++)
            if (outerFirst)
            {
                await generator.WriteAsync(outer, _out).ConfigureAwait(false);
                await generator.WriteAsync(inner, nested).ConfigureAwait(false);
            }
            else
            {
                await generator.WriteAsync(inner, nested).ConfigureAwait(false);
                await generator.WriteAsync(outer, _out).ConfigureAwait(false);
            }

        (await generator.CheckAsync(outer, _out).ConfigureAwait(false)).Should().BeEmpty();
        (await generator.CheckAsync(inner, nested).ConfigureAwait(false)).Should().BeEmpty();
        File.Exists(Path.Combine(nested, "index.g.ts")).Should().BeTrue();
        File.Exists(Path.Combine(nested, "client.g.ts")).Should().BeTrue();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task A_client_in_a_folder_another_writes_into_is_refused(bool outerFirst)
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        var nested = Path.Combine(_out, "documents");
        await generator.WriteAsync(options, outerFirst ? _out : nested).ConfigureAwait(false);

        var write = async () =>
            await generator.WriteAsync(options, outerFirst ? nested : _out).ConfigureAwait(false);
        var documents = new ClientDocumentFile[] { StartRunFile };
        var withDocuments = async () =>
            await generator
                .WriteAsync(options with { Documents = documents }, _out)
                .ConfigureAwait(false);

        if (outerFirst)
            (
                await write.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
            ).WithMessage("*a folder the client at*writes into*");
        else
            (
                await withDocuments
                    .Should()
                    .ThrowAsync<InvalidOperationException>()
                    .ConfigureAwait(false)
            ).WithMessage("*holds another client trax client wrote*");
        File.Exists(Path.Combine(nested, "index.g.ts")).Should().Be(!outerFirst);
    }

    [Test]
    public async Task Write_leaves_a_g_ts_file_it_did_not_generate()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        Directory.CreateDirectory(Path.Combine(_out, "app"));
        var own = Path.Combine(_out, "app", "routes.g.ts");
        await File.WriteAllTextAsync(own, "// Generated by the app's router. Do not edit.\n");
        var empty = Path.Combine(_out, "empty.g.ts");
        await File.WriteAllTextAsync(empty, "");

        await generator.WriteAsync(options, _out).ConfigureAwait(false);

        File.Exists(own).Should().BeTrue("another generator's file under --out is not this one's");
        File.Exists(empty).Should().BeTrue();
        (await generator.CheckAsync(options, _out).ConfigureAwait(false))
            .Should()
            .BeEmpty("only this generator's files are stale");
    }

    [Test]
    public async Task Write_does_not_follow_a_symlinked_folder()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        var shared = Path.Combine(_tools, "shared");
        Directory.CreateDirectory(shared);
        var outside = Path.Combine(shared, "outside.g.ts");
        await File.WriteAllTextAsync(outside, GeneratedText);
        Directory.CreateDirectory(_out);
        Directory.CreateSymbolicLink(Path.Combine(_out, "shared"), shared);

        await generator.WriteAsync(options, _out).ConfigureAwait(false);

        File.Exists(outside).Should().BeTrue("a linked folder leads outside --out");
        (await generator.CheckAsync(options, _out).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task Write_does_not_write_through_a_symlinked_machine_folder()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        var outside = Path.Combine(_tools, "elsewhere");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(_out);
        Directory.CreateSymbolicLink(Path.Combine(_out, "checkout"), outside);

        var write = async () => await generator.WriteAsync(options, _out).ConfigureAwait(false);

        (
            await write.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*checkout is a link*");
        Directory
            .EnumerateFileSystemEntries(outside)
            .Should()
            .BeEmpty("nothing is written through the link");
        File.Exists(Path.Combine(_out, "format.g.ts"))
            .Should()
            .BeFalse("the link is refused before anything is written");
        var check = async () => await generator.CheckAsync(options, _out).ConfigureAwait(false);
        (
            await check.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*checkout is a link*");
    }

    [Test]
    public async Task Write_does_not_write_through_a_symlinked_file()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        var outside = Path.Combine(_tools, "outside.ts");
        await File.WriteAllTextAsync(outside, "// the app's own\n");
        Directory.CreateDirectory(_out);
        File.CreateSymbolicLink(Path.Combine(_out, "format.g.ts"), outside);

        var write = async () => await generator.WriteAsync(options, _out).ConfigureAwait(false);

        (
            await write.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*format.g.ts is a link*");
        (await File.ReadAllTextAsync(outside).ConfigureAwait(false))
            .Should()
            .Be("// the app's own\n");
    }

    [Test]
    public async Task Write_skips_a_file_that_is_not_regular()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("A FIFO is made with mkfifo.");
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        Directory.CreateDirectory(_out);
        var fifo = Path.Combine(_out, "x.g.ts");
        await MakeFifo(fifo).ConfigureAwait(false);
        await MakeFifo(Path.Combine(_out, "format.g.ts")).ConfigureAwait(false);

        // Opening a FIFO blocks until something writes to it, so a run that opens one never ends.
        var run = Task.Run(async () =>
        {
            var drift = await generator.CheckAsync(options, _out).ConfigureAwait(false);
            await generator.WriteAsync(options, _out).ConfigureAwait(false);
            return drift;
        });

        var finished = true;
        using (var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            try
            {
                await run.WaitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                finished = false;
            }
        }
        finished.Should().BeTrue("a FIFO under --out is never opened");
        (await run.ConfigureAwait(false))
            .Should()
            .Contain(new ClientDrift("format.g.ts", DriftStatus.Drifted));
        (await run.ConfigureAwait(false)).Select(d => d.Path).Should().NotContain("x.g.ts");
        File.Exists(fifo).Should().BeTrue("a file that is not regular is not this generator's");
        (await File.ReadAllTextAsync(Path.Combine(_out, "format.g.ts")).ConfigureAwait(false))
            .Should()
            .StartWith(GeneratedText);
        (await generator.CheckAsync(options, _out).ConfigureAwait(false)).Should().BeEmpty();
    }

    private static async Task MakeFifo(string path)
    {
        using var mkfifo = System
            .Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("mkfifo", [path])
                {
                    UseShellExecute = false,
                }
            )
            .NotNull();
        await mkfifo.WaitForExitAsync().ConfigureAwait(false);
        mkfifo.ExitCode.Should().Be(0);
    }

    private static string TwinHeader(string id, string kind) =>
        $"// AUTO-GENERATED from {id}.ir.json by {(string.Equals(kind, "machine", StringComparison.Ordinal) ? "generateMachineFactory" : "generateContextTypes")}. Do not edit by hand.\n";

    [Test]
    public async Task Write_leaves_a_twin_trax_machine_generate_wrote_for_a_machine_the_manifest_does_not_serve()
    {
        var generator = new ClientGenerator(new FakeNodeRunner());
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        await generator.WriteAsync(options, _out).ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(_out, "reminder"));
        var machine = Path.Combine(_out, "reminder", "reminder.machine.g.ts");
        var contexts = Path.Combine(_out, "reminder", "reminder.contexts.g.ts");
        await File.WriteAllTextAsync(machine, TwinHeader("reminder", "machine"))
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(contexts, TwinHeader("reminder", "contexts"))
            .ConfigureAwait(false);
        var other = Path.Combine(_out, "schema.g.ts");
        await File.WriteAllTextAsync(
                other,
                "// AUTO-GENERATED from schema.graphql by another tool.\n"
            )
            .ConfigureAwait(false);

        await generator.WriteAsync(options, _out).ConfigureAwait(false);

        File.Exists(machine)
            .Should()
            .BeTrue(
                "the previous run did not serve reminder, so trax client did not write its twin"
            );
        File.Exists(contexts).Should().BeTrue();
        File.Exists(other).Should().BeTrue("a header that only starts like a twin's is not one");
        (await generator.CheckAsync(options, _out).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task Write_removes_the_twin_of_a_machine_the_previous_run_served()
    {
        var node = new FakeNodeRunner
        {
            MachineContent = TwinHeader("checkout", "machine"),
            ContextsContent = TwinHeader("checkout", "contexts"),
        };
        var generator = new ClientGenerator(node);
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        await generator.WriteAsync(options, _out).ConfigureAwait(false);
        // The client module goes first, so only the manifest.json the previous run wrote says checkout was served.
        File.Delete(Path.Combine(_out, "checkout", "checkout.client.g.ts"));

        var machineless = options with
        {
            Manifest = ClientManifests.Sample() with { Machines = [] },
        };
        (await generator.CheckAsync(machineless, _out).ConfigureAwait(false))
            .Select(d => d.Path)
            .Should()
            .Contain(["checkout/checkout.machine.g.ts", "checkout/checkout.contexts.g.ts"]);
        await generator.WriteAsync(machineless, _out).ConfigureAwait(false);

        Directory.Exists(Path.Combine(_out, "checkout")).Should().BeFalse();
        (await generator.CheckAsync(machineless, _out).ConfigureAwait(false)).Should().BeEmpty();
    }

    [TestCase("configureTrax")]
    [TestCase("TraxOperationError")]
    public async Task A_type_that_takes_a_runtime_name_is_refused(string name)
    {
        var typeManifest = ClientManifests.Sample() with
        {
            Types = ClientManifests.TrainTypes + $"\nenum {name} {{ X }}\n",
        };

        (
            await ((Func<Task>)(() => RenderAsync(typeManifest)))
                .Should()
                .ThrowAsync<InvalidOperationException>()
                .ConfigureAwait(false)
        ).WithMessage($"*runtime*{name}*");
    }

    [TestCase("options")]
    [TestCase("sendDocument")]
    [TestCase("startRunDocumentId")]
    public async Task A_variable_named_as_a_local_of_the_function_is_refused(string name)
    {
        var document = StartRunFile with
        {
            Text = ClientManifests.StartRunDocument.Replace(
                "$input",
                "$" + name,
                StringComparison.Ordinal
            ),
        };

        var render = async () =>
            await RenderDocumentsAsync(ClientManifests.Sample(), document).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage($"*'{name}'*");
    }

    // A document's module imports sendDocument and declares <function>DocumentId beside the types it imports.
    [Test]
    public async Task A_document_whose_function_is_a_name_its_module_imports_is_refused()
    {
        var document = StartRunFile with
        {
            Text = ClientManifests.StartRunDocument.Replace(
                "mutation StartRun",
                "mutation SendDocument",
                StringComparison.Ordinal
            ),
        };

        var render = async () =>
            await RenderDocumentsAsync(ClientManifests.Sample(), document).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*'sendDocument'*its generated modules use*");
    }

    [TestCase("startRunDocumentId")]
    [TestCase("sendDocument")]
    public async Task A_type_named_as_a_name_a_document_module_declares_or_imports_is_refused(
        string name
    )
    {
        var manifest = ClientManifests.Sample() with
        {
            Types = ClientManifests.TrainTypes + $"\nenum {name} {{ X }}\n",
        };

        var render = async () =>
            await RenderDocumentsAsync(manifest, StartRunFile).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage($"*'{name}'*");
    }

    [Test]
    public async Task A_reserved_word_the_runtime_forbids_is_refused()
    {
        foreach (var word in new[] { "Eval", "Arguments" })
        {
            var document = StartRunFile with
            {
                Text = ClientManifests.StartRunDocument.Replace(
                    "mutation StartRun",
                    $"mutation {word}",
                    StringComparison.Ordinal
                ),
            };
            var render = async () =>
                await RenderDocumentsAsync(ClientManifests.Sample(), document)
                    .ConfigureAwait(false);
            (
                await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
            ).WithMessage($"*'{word.ToLowerInvariant()}'*reserves*");
        }
    }

    [Test]
    public async Task A_progress_module_with_no_followable_train_names_none()
    {
        var train = ClientManifests.Sample().Trains[0] with { Progress = false };

        var progress = Text(
            await RenderAsync(ClientManifests.Sample() with { Trains = [train] })
                .ConfigureAwait(false),
            "progress.g.ts"
        );

        progress.Should().NotContain("can be followed");
    }

    [Test]
    public void The_generator_and_the_runtime_name_one_client_format()
    {
        var source = Path.Combine(EngineSrc(), "clientFormat.ts");
        var match = ClientFormatDeclaration.Match(File.ReadAllText(source));

        match
            .Success.Should()
            .BeTrue("clientFormat.ts declares `export const clientFormat = <n>;`");
        int.Parse(match.Groups["format"].Value, System.Globalization.CultureInfo.InvariantCulture)
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
            var candidate = Path.Combine(dir.FullName, "Trax.StateMachine", "ts", "src");
            if (Directory.Exists(candidate))
                return candidate;
        }
        Assert.Ignore("Trax.StateMachine/ts not found (isolated build).");
        return "";
    }

    [GeneratedRegex(
        @"^export const clientFormat = (?<format>\d+);$",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex ClientFormatDeclaration { get; }
}
