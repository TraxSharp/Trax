using System.Runtime.Loader;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trax.Cli.Machines;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A machine often lives in a project beside types the CLI cannot load: a controller or a SignalR hub built on
/// a shared framework the CLI does not carry, or a class deriving from a dependency that is not deployed.
/// Asking the assembly for all its types then throws, and that must not hide the machine beside them. These
/// emit such an assembly with Roslyn: a machine plus a class deriving from a type in a dependency that is then
/// left out of the directory.
/// </summary>
public class MachineLoaderUnloadableTypeTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-loader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Test]
    public void DiscoverMachines_finds_a_machine_beside_a_type_whose_base_cannot_load()
    {
        var path = EmitConsumer(withMachine: true);
        var assembly = new AssemblyLoadContext(null).LoadFromAssemblyPath(path);

        var machines = MachineLoader.DiscoverMachines(assembly);

        machines.Select(t => t.FullName).Should().Equal("Consumer.TurnstileMachine");
    }

    [Test]
    public void Load_of_an_assembly_whose_only_types_cannot_load_names_what_is_missing()
    {
        var path = EmitConsumer(withMachine: false);

        var act = () => MachineLoader.Load(path, null);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("No machines found*could not be loaded*MissingDependency*");
    }

    private string EmitConsumer(bool withMachine)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        // The dependency is compiled but never written next to the consumer, so its type cannot load.
        var dependency = Emit(
            $"MissingDependency{suffix}",
            "namespace Missing; public class ControllerBase { }",
            platform
        );

        var machine = withMachine
            ? """
                public enum S { Locked, Unlocked }
                public enum T { Coin }
                public sealed class TurnstileMachine : Machine<S, T>
                {
                    protected override void Configure(IMachineBuilder<S, T> m)
                    {
                        m.Id("turnstile").Version(1).StartsAt(S.Locked, () => new JsonObject());
                        m.In(S.Locked).On(T.Coin).To(S.Unlocked);
                    }
                }
                """
            : "";
        var source = $$"""
            using System.Text.Json.Nodes;
            using Trax.Effect.StateMachine;
            using Trax.Effect.StateMachine.Persistence;
            namespace Consumer;
            public class PlayersController : Missing.ControllerBase { }
            {{machine}}
            """;

        var image = Emit(
            $"Consumer{suffix}",
            source,
            [.. platform, MetadataReference.CreateFromImage(dependency)]
        );
        var path = Path.Combine(_dir, $"Consumer{suffix}.dll");
        File.WriteAllBytes(path, image);
        return path;
    }

    private static byte[] Emit(string name, string source, IEnumerable<MetadataReference> refs)
    {
        using var stream = new MemoryStream();
        var result = CSharpCompilation
            .Create(
                name,
                [CSharpSyntaxTree.ParseText(source)],
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            )
            .Emit(stream);
        result
            .Success.Should()
            .BeTrue(string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
        return stream.ToArray();
    }
}
