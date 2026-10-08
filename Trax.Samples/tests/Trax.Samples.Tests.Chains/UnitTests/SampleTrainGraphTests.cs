using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Samples.Tests.Chains.UnitTests;

/// <summary>
/// Every sample train's declared chain, drawn as a <see cref="ChainGraph"/>, matches the golden
/// committed beside this file as <c>Goldens/&lt;TrainName&gt;.chain.json</c>.
///
/// <para>A golden changes only when someone means it to. When a graph differs from its golden, or
/// has none, the test fails and writes <c>&lt;TrainName&gt;.chain.json.received</c> next to it;
/// review it, and rename it over the golden if the change is the one you meant. Nothing is
/// overwritten for you, because a golden that updates itself proves nothing.</para>
///
/// <para>The golden is the graph's canonical JSON (<see cref="ChainGraph.ToJson"/>) re-indented,
/// with its properties in the same order, so a change to one step reads as a change to a few lines
/// rather than to one very long one.</para>
///
/// <para>A train is built with its parameterless constructor, not from its sample's container.
/// Reading a chain resolves and runs no junction, so nothing the container would supply is used,
/// and building a sample's container would need its database and broker. The templates' trains are
/// left out: they are scaffolded into a consumer's repository, not run here.</para>
/// </summary>
[TestFixture]
public class SampleTrainGraphTests
{
    private const string Solution = "Trax.Samples.slnx";

    private static readonly Regex DefinesATrain = new(
        @":\s*(ServiceTrain|Train)<",
        RegexOptions.Compiled
    );

    private static readonly Lazy<string> SamplesRoot = new(FindSamplesRoot);

    private static string GoldensDirectory =>
        Path.Combine(
            SamplesRoot.Value,
            "tests",
            "Trax.Samples.Tests.Chains",
            "UnitTests",
            "Goldens"
        );

    private static readonly Lazy<IReadOnlyList<Type>> Trains = new(FindTrains);

    private static IEnumerable<TestCaseData> EveryTrain() =>
        Trains.Value.Select(t => new TestCaseData(t).SetArgDisplayNames(t.Name));

    [TestCaseSource(nameof(EveryTrain))]
    public void DeclaredGraph_MatchesItsGolden(Type train)
    {
        var expected = Render(GraphOf(train));
        var golden = Path.Combine(GoldensDirectory, $"{train.Name}.chain.json");
        var received = golden + ".received";

        if (File.Exists(golden) && File.ReadAllText(golden) == expected)
        {
            File.Delete(received);
            return;
        }

        Directory.CreateDirectory(GoldensDirectory);
        File.WriteAllText(received, expected);

        Assert.Fail(
            File.Exists(golden)
                ? $"The declared chain of {train.FullName} no longer matches its golden. The graph it "
                    + $"declares now is in {received}. Review the difference, and if it is the change "
                    + $"you meant, rename that file over {Path.GetFileName(golden)}."
                : $"{train.FullName} has no golden. Its graph is in {received}. Review it, and if it "
                    + $"is right, rename it to {Path.GetFileName(golden)}."
        );
    }

    [Test]
    public void TrainNames_AreUnique_SoEachHasOneGolden()
    {
        Trains
            .Value.GroupBy(t => t.Name)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(", ", g.Select(t => t.FullName)))
            .Should()
            .BeEmpty("a golden is named after its train, so two trains of one name would share it");
    }

    [Test]
    public void EveryGolden_NamesATrainThatStillExists()
    {
        var names = Trains.Value.Select(t => $"{t.Name}.chain.json").ToHashSet();

        Directory
            .EnumerateFiles(GoldensDirectory, "*.chain.json")
            .Select(Path.GetFileName)
            .Where(file => !names.Contains(file!))
            .Should()
            .BeEmpty("a golden whose train was renamed or removed checks nothing; delete it");
    }

    [Test]
    public void EverySampleProjectWithATrain_IsReadHere()
    {
        var loaded = SampleAssemblies().Select(a => a.GetName().Name).ToHashSet();
        var samples = Path.Combine(SamplesRoot.Value, "samples");

        Directory
            .EnumerateFiles(samples, "*.csproj", SearchOption.AllDirectories)
            .Where(project => DeclaresATrain(Path.GetDirectoryName(project)!))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !loaded.Contains(name))
            .Should()
            .BeEmpty(
                "a sample project that defines a train needs a ProjectReference in "
                    + "Trax.Samples.Tests.Chains.csproj, or its trains have no golden"
            );
    }

    [Test]
    public void ResearchTopicTrain_DrawsItsSwitchAndScaleTracks()
    {
        var research = Trains.Value.Single(t => t.Name == "ResearchTopicTrain");
        var graph = GraphOf(research);

        var routing = graph.Nodes.Where(n => n.Tracks.Count > 0).ToList();
        routing.Select(n => n.Id).Should().Equal("Switch<Source>#0", "Scale<Depth>#0");
        routing[0].Tracks.Select(t => t.Name).Should().Equal("Web", "Papers", "Wiki");
        routing[0]
            .Tracks[1]
            .Nodes.Select(n => n.Id)
            .Should()
            .Equal("Switch<Source>#0/Papers/SearchPapers#0");
        routing[1].Tracks.Select(t => t.Name).Should().Equal("Skim", "CrossCheck");
    }

    /// <summary>
    /// The graph's canonical JSON, indented two spaces with <c>\n</c> line breaks and a trailing
    /// newline. The order of properties and array items is the canonical one, so the rendering is as
    /// deterministic as <see cref="ChainGraph.ToJson"/> itself.
    /// </summary>
    private static string Render(ChainGraph graph)
    {
        using var canonical = JsonDocument.Parse(graph.ToJson());
        using var buffer = new MemoryStream();

        using (
            var json = new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions
                {
                    Indented = true,
                    NewLine = "\n",
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }
            )
        )
            canonical.WriteTo(json);

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static ChainGraph GraphOf(Type train)
    {
        var declared = TrainBase(train)!;
        var arguments = declared.GetGenericArguments();
        var instance =
            Activator.CreateInstance(train)
            ?? throw new InvalidOperationException($"{train.FullName} could not be built.");
        var chain = (ChainRecorder)
            train
                .GetMethod(nameof(Train<,>.DeclaredChain), Type.EmptyTypes)!
                .Invoke(instance, null)!;

        return ChainGraph.From(chain, train, arguments[0], arguments[1]);
    }

    private static Type? TrainBase(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Train<,>))
                return current;

        return null;
    }

    private static IReadOnlyList<Type> FindTrains() =>
        SampleAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .Where(t => TrainBase(t) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The sample assemblies this project's references copied beside it. Loading them by file
    /// rather than through a type in each means a new reference needs no code here to be read.
    /// </summary>
    private static IEnumerable<Assembly> SampleAssemblies()
    {
        var self = typeof(SampleTrainGraphTests).Assembly.GetName().Name;

        return Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Trax.Samples.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != self)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => Assembly.Load(new AssemblyName(name!)));
    }

    private static bool DeclaresATrain(string projectDirectory) =>
        Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Any(file => DefinesATrain.IsMatch(File.ReadAllText(file)));

    private static string FindSamplesRoot()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
            if (File.Exists(Path.Combine(dir.FullName, Solution)))
                return dir.FullName;

        throw new InvalidOperationException(
            $"No {Solution} found walking up from '{AppContext.BaseDirectory}'."
        );
    }
}
