using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin.Lexical;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// What reading a lexicon gave: the glossary it elaborates to and that glossary's canonical JSON, the file
/// <c>trax machine from-gherkin</c> writes beside the IRs; or the problems, each at its lexicon line, and no glossary.
/// </summary>
public sealed record LexiconReading(
    FeatureGlossary? Glossary,
    string? Json,
    IReadOnlyList<FeatureProblem> Problems
)
{
    /// <summary>Whether the lexicon elaborated to a glossary.</summary>
    public bool Succeeded => Glossary is not null;
}

/// <summary>
/// The lexicon: the file people write to say what their features' words mean, line by line in the style of Gherkin
/// (<c>conformance/lexicon.md</c>). It elaborates to the glossary (<c>glossary.json</c>), filling in what follows a
/// rule, and every problem names a lexicon line, never a key of the glossary.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-machines">SDK reference</seealso>
public static partial class Lexicon
{
    /// <summary>A lexicon's file extension.</summary>
    public const string Extension = ".lexicon";

    /// <summary>
    /// Reads a lexicon. A train's output fields come from its <c>Returns:</c> line, and its input fields from its
    /// <c>Takes:</c> and <c>Input:</c> lines.
    /// </summary>
    /// <param name="path">The lexicon's path, as every problem names it.</param>
    /// <param name="text">The lexicon's text.</param>
    public static LexiconReading Read(string path, string text) => Read(path, text, null);

    /// <summary>
    /// Reads a lexicon, finding each train it runs in <paramref name="trains"/> by its full name and reading its
    /// input and output fields from its records, as the host does. <c>Returns:</c> and <c>Takes:</c> lines are then
    /// optional, and one that disagrees with the record is a problem at its line.
    /// </summary>
    public static LexiconReading Read(string path, string text, IEnumerable<Assembly> trains)
    {
        ArgumentNullException.ThrowIfNull(trains);
        return Read(path, text, (IReadOnlyCollection<Assembly>)trains.ToList());
    }

    /// <summary>
    /// The one lexicon in <paramref name="directory"/>, read; null when it has none. Two lexicons in one directory are
    /// a problem: a features directory has one, holding any number of machines.
    /// </summary>
    public static LexiconReading? ReadDirectory(
        string directory,
        IEnumerable<Assembly>? trains = null
    )
    {
        ArgumentNullException.ThrowIfNull(directory);
        var found = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*" + Extension).Order(StringComparer.Ordinal).ToList()
            : [];
        if (found.Count == 0)
            return null;
        if (found.Count > 1)
            return new LexiconReading(
                null,
                null,
                [
                    new FeatureProblem(
                        found[1],
                        0,
                        $"{Path.GetFileName(found[0])} is this directory's lexicon already; a features directory "
                            + "has one, holding any number of machines."
                    ),
                ]
            );
        var text = File.ReadAllText(found[0]);
        return trains is null ? Read(found[0], text) : Read(found[0], text, trains);
    }

    private static LexiconReading Read(
        string path,
        string text,
        IReadOnlyCollection<Assembly>? trains
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        LexiconReading Refused(IEnumerable<LexProblem> problems) =>
            new(
                null,
                null,
                problems
                    .OrderBy(p => p.Line)
                    .Select(p => new FeatureProblem(path, p.Line, p.Message))
                    .ToList()
            );

        var (lines, problems) = LexiconSyntax.Lines(text);
        var (doc, parsed) = LexiconParser.Parse(lines);
        problems.AddRange(parsed);
        if (problems.Count > 0)
            return Refused(problems);
        var (json, map, elaborated) = LexiconElaborator.Elaborate(
            doc,
            trains is null ? null : name => TrainShapes.Find(trains, name)
        );
        if (elaborated.Count > 0)
            return Refused(elaborated);

        var source = new LexiconSource(path, map);
        var written = GlossaryWriter.Write(json);
        try
        {
            var glossary = Glossary.Read(written);
            return new LexiconReading(FeatureGlossary.From(glossary, source), written, []);
        }
        catch (PointedFormatException ex)
        {
            return Refused([
                new LexProblem(source.LineOf(ex.Pointer), source.Message(ex.Pointer, ex.Problem)),
            ]);
        }
    }
}

/// <summary>
/// Where each key of an elaborated glossary came from in its lexicon, so a problem the glossary reader or derivation
/// finds is reported at the lexicon line, in the lexicon's words.
/// </summary>
internal sealed partial class LexiconSource(string path, IReadOnlyDictionary<string, int> lines)
{
    internal string Path { get; } = path;

    /// <summary>The line of the deepest key a pointer passes through that the lexicon wrote, or 0.</summary>
    internal int LineOf(string? pointer)
    {
        var at = pointer ?? "";
        while (true)
        {
            if (lines.TryGetValue(at, out var line))
                return line;
            var slash = at.LastIndexOf('/');
            if (slash < 0)
                return 0;
            at = at[..slash];
        }
    }

    // The glossary's keys, as the lexicon says them.
    private static readonly Dictionary<string, string> Keywords = new(StringComparer.Ordinal)
    {
        ["version"] = "Version:",
        ["actors"] = "Actor:",
        ["roles"] = "Roles:",
        ["anyAuthenticated"] = "Any signed-in",
        ["ownedBy"] = "Owned by:",
        ["system"] = "System",
        ["start"] = "Started by:",
        ["initial"] = "Begins in:",
        ["context"] = "Context:",
        ["initialContext"] = "Starts with:",
        ["states"] = "State:",
        ["requires"] = "Requires:",
        ["triggers"] = "Trigger:",
        ["input"] = "Input:",
        ["reduce"] = "Sets:",
        ["effect"] = "Effect:",
        ["guard"] = "Guarded by:",
        ["guards"] = "Guard:",
        ["holds"] = "Holds:",
        ["fails"] = "Fails:",
        ["invokes"] = "Runs:",
        ["output"] = "Returns:",
        ["outcomes"] = "Outcome:",
        ["examples"] = "Example:",
        ["links"] = "Link:",
        ["rules"] = "Rule:",
        ["reductions"] = "Reduction:",
        ["migrations"] = "Migrates from:",
        ["differential"] = "Differential",
        ["views"] = "View:",
        ["reads"] = "Reads:",
        ["fields"] = "Field:",
        ["labels"] = "Label:",
    };

    /// <summary>
    /// A glossary problem in the lexicon's terms: led by the keyword of the deepest key it is about, and with any other
    /// key it names given as its line.
    /// </summary>
    internal string Message(string? pointer, string problem)
    {
        var said = Pointer()
            .Replace(problem, m => $"line {LineOf(m.Groups[1].Value)}{m.Groups[2].Value}");
        var keyword = (pointer ?? "")
            .Split('/')
            .Reverse()
            .Select(s => Keywords.GetValueOrDefault(s))
            .FirstOrDefault(k => k is not null);
        return keyword is null ? said : $"'{keyword}' {said}";
    }

    // A pointer the reader names inside its message: "is also at /machines/order/states/Draft: a step …".
    [GeneratedRegex(@"(/(?:actors|machines|views)/.*?)(:\s|\.$|$)", RegexOptions.CultureInvariant)]
    private static partial Regex Pointer();
}

/// <summary>A train's input and output fields from its records, found in assemblies by its full name.</summary>
internal static class TrainShapes
{
    private const string ServiceTrain = "Trax.Effect.Services.ServiceTrain.IServiceTrain`2";

    internal static TrainShape Find(IReadOnlyCollection<Assembly> assemblies, string name)
    {
        var found = assemblies
            .Select(a => a.GetType(name, throwOnError: false))
            .OfType<Type>()
            .Distinct()
            .ToList();
        if (found.Count == 0)
            return new TrainShape(
                null,
                null,
                $"no train {name} is in the assemblies given ({string.Join(", ", assemblies.Select(a => a.GetName().Name))})."
            );
        if (found.Count > 1)
            return new TrainShape(
                null,
                null,
                $"{name} is in more than one of the assemblies given."
            );
        var train = found[0];
        // Compared by name, not identity: the CLI loads a hub's assemblies beside its own copy of Trax.Effect.
        var service = new[] { train }
            .Concat(train.GetInterfaces())
            .FirstOrDefault(i =>
                i.IsGenericType && i.GetGenericTypeDefinition().FullName == ServiceTrain
            );
        if (service is null)
            return new TrainShape(
                null,
                null,
                $"{name} is not a train interface (IServiceTrain<TIn, TOut>)."
            );
        var arguments = service.GetGenericArguments();
        try
        {
            return new TrainShape(Fields(arguments[0]), Fields(arguments[1]), null);
        }
        catch (InvalidOperationException ex)
        {
            return new TrainShape(null, null, $"{name}'s records have no schema: {ex.Message}");
        }
    }

    private static JsonArray Fields(Type record) =>
        (JsonArray)IrExporter.WriteSchema(SchemaReflection.ForInput(record))["fields"]!.DeepClone();
}
