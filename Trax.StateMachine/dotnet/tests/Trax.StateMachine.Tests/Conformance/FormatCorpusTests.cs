using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Conformance;

/// <summary>
/// The glossary and the host file each have a schema in <c>conformance/</c> and a hand-written reader in C#, and the
/// two must not drift. <c>conformance/formats/&lt;format&gt;/</c> holds documents in three directories: <c>valid</c>,
/// which both accept; <c>invalid</c>, which both refuse; and <c>refused</c>, which the schema accepts and the reader
/// refuses because names in it do not agree (an owner that is not an actor, a phrase used twice), something a schema
/// cannot say. A file in the wrong directory, for either of them, fails here.
/// </summary>
public class FormatCorpusTests
{
    private const string Adr =
        "Trax.Docs/adr/0058-features-and-a-glossary-are-a-source-of-machines.md";

    public static IEnumerable<string> Formats => ["glossary", "host"];

    [TestCaseSource(nameof(Formats))]
    public void The_reader_files_every_corpus_document_where_it_is(string format)
    {
        var wrong = Corpus(format)
            .Where(d => Reads(format, d.Text) != (d.Kind == "valid"))
            .Select(d => $"{d.Kind}/{d.Name}: {Why(format, d.Text) ?? "accepted"}")
            .ToList();

        wrong
            .Should()
            .BeEmpty(
                $"the C# {format} reader accepts exactly conformance/formats/{format}/valid ({Adr})"
            );
    }

    [TestCaseSource(nameof(Formats))]
    [Category(Categories.CrossEngine)]
    public void The_schema_files_every_corpus_document_where_it_is(string format)
    {
        var corpus = Corpus(format);
        var verdicts = TwinEngine.ValidateFormat(format, corpus.Select(d => d.Text).ToList());

        var wrong = corpus
            .Zip(verdicts)
            .Where(p => (p.Second.Count == 0) != (p.First.Kind != "invalid"))
            .Select(p =>
                $"{p.First.Kind}/{p.First.Name}: {(p.Second.Count == 0 ? "accepted" : string.Join("; ", p.Second.Take(2)))}"
            )
            .ToList();

        wrong
            .Should()
            .BeEmpty(
                $"conformance/{format}.schema.json refuses exactly conformance/formats/{format}/invalid ({Adr})"
            );
    }

    [TestCaseSource(nameof(Formats))]
    public void Every_corpus_directory_holds_documents(string format)
    {
        var kinds = Corpus(format).GroupBy(d => d.Kind).ToDictionary(g => g.Key, g => g.Count());

        kinds
            .Keys.Should()
            .BeEquivalentTo(
                ["valid", "invalid", "refused"],
                $"the {format} corpus has all three directories"
            );
    }

    private static bool Reads(string format, string text) => Why(format, text) is null;

    private static string? Why(string format, string text)
    {
        try
        {
            _ = format == "glossary" ? Glossary.Read(text) : (object)HostFile.Read(text);
            return null;
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    private static List<(string Kind, string Name, string Text)> Corpus(string format) =>
        Directory
            .GetDirectories(Path.Combine(FixturePaths.ConformanceRoot, "formats", format))
            .SelectMany(dir =>
                Directory
                    .GetFiles(dir, "*.json")
                    .Order(StringComparer.Ordinal)
                    .Select(f =>
                        (
                            Path.GetFileName(dir),
                            Path.GetFileNameWithoutExtension(f),
                            File.ReadAllText(f)
                        )
                    )
            )
            .ToList();
}
