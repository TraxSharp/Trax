using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin.Lexical;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// The lexicon: the file people write to say what their features' words mean, line by line in the style of Gherkin
/// (<c>conformance/lexicon.md</c>). It elaborates to the glossary (<c>glossary.json</c>), filling in what follows a
/// rule, and every problem names a lexicon line, never a key of the glossary.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-machines">SDK reference</seealso>
public static class Lexicon
{
    /// <summary>A lexicon's file extension.</summary>
    public const string Extension = ".lexicon";

    private const string GlossaryFileName = "glossary.json";

    /// <summary>
    /// Reads a lexicon. A train's output fields come from its <c>Returns:</c> line, and its input fields from its
    /// <c>Takes:</c> and <c>Input:</c> lines.
    /// </summary>
    /// <param name="path">The lexicon's path, as every problem names it.</param>
    /// <param name="text">The lexicon's text.</param>
    public static LexiconReading Read(string path, string text) => Read(path, text, trains: null);

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
    /// a problem: a features directory has one, holding any number of machines. So is a <c>glossary.json</c> beside
    /// the lexicon, which is derived from it and written beside the machines, so a copy here could only disagree.
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
                Glossary: null,
                Json: null,
                [
                    new FeatureProblem(
                        found[1],
                        0,
                        $"{Path.GetFileName(found[0])} is this directory's lexicon already; a features directory "
                            + "has one, holding any number of machines."
                    ),
                ]
            );
        var beside = Path.Combine(directory, GlossaryFileName);
        if (File.Exists(beside))
            return new LexiconReading(
                Glossary: null,
                Json: null,
                [
                    new FeatureProblem(
                        beside,
                        0,
                        $"{GlossaryFileName} is beside the lexicon {Path.GetFileName(found[0])}. The glossary is "
                            + "derived from the lexicon and written beside the machines, so delete this copy."
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
                Glossary: null,
                Json: null,
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
