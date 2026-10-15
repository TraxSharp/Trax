using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Cli.Clients;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;
using Trax.Effect.StateMachine;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax client</c> renders a frontend's client from a host's manifest: every module checks the client format
/// before anything else, a machine's documents and a train's id are sent by id, a train's input and answer are typed
/// from the manifest's SDL, anything from the manifest is written as data (names checked, strings as literals,
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

    private IReadOnlyList<ClientFile> Render(ClientManifest? manifest = null) =>
        new ClientGenerator(new FakeNodeRunner()).Render(
            new ClientGenerateOptions(manifest ?? ClientManifests.Sample(), ToolsDir: _tools)
        );

    private static string Text(IReadOnlyList<ClientFile> files, string path) =>
        files.Single(f => f.Path == path).Text;

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
        text.Should().Contain("rememberKey: \"trax.checkout\"");
    }

    [Test]
    public void A_train_takes_its_input_and_options_and_answers_its_type()
    {
        var files = Render();
        var train = Text(files, "trains/startRun.g.ts");
        var types = Text(files, "types.g.ts");

        train
            .Should()
            .Contain(
                "export function startRun(input: StartRunInput, options: { mode?: ExecutionMode | null; priority?: number | null } = {}): Promise<StartRunResponse>"
            )
            .And.Contain("[\"dispatch\", \"startRun\"]")
            .And.Contain("holding one of the roles recovery-user");
        types.Should().Contain("export type ExecutionMode = \"RUN\" | \"QUEUE\";");
        types.Should().Contain("  scenario: string;").And.Contain("  crashOnce?: boolean | null;");
        types
            .Should()
            .Contain("  metadataId: number | null;")
            .And.Contain("  steps: Array<string>;");
        types.Should().NotContain("DispatchMutations", "a type that holds operations is not data");
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
        var options = new ClientGenerateOptions(ClientManifests.Sample(), ToolsDir: _tools);
        generator.Write(options, _out);
        generator.Check(options, _out).Should().BeEmpty();

        var client = Path.Combine(_out, "client.g.ts");
        File.WriteAllText(client, File.ReadAllText(client).Replace("\n", "\r\n"));
        generator.Check(options, _out).Should().BeEmpty("a checkout that writes CRLF is not drift");

        File.AppendAllText(Path.Combine(_out, "trains", "startRun.g.ts"), "// edited\n");
        File.WriteAllText(Path.Combine(_out, "trains", "gone.g.ts"), "");
        generator
            .Check(options, _out)
            .Select(d => (d.Path, d.Status))
            .Should()
            .BeEquivalentTo([
                ("trains/startRun.g.ts", DriftStatus.Drifted),
                ("trains/gone.g.ts", DriftStatus.Drifted),
            ]);
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
