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

    // A refused document is refused for the one thing its name says, not for a mistake made while writing it.
    [TestCase("glossary", "view-name-not-a-graphql-name", "has no GraphQL name")]
    [TestCase("glossary", "view-names-fold-together", "is TheShopSign in GraphQL")]
    [TestCase("glossary", "view-row-types-collide", "PinnedStickyNoteRow")]
    [TestCase("glossary", "brace-in-state-phrase", "may hold a {placeholder}")]
    [TestCase("glossary", "invoke-output-field-twice", "repeats the field 'approved'")]
    [TestCase("glossary", "context-field-named-owner", "has a field named '$owner'")]
    [TestCase("glossary", "effect-trigger-input-not-receipt", "with the receipt alone")]
    [TestCase(
        "glossary",
        "effect-trigger-with-input-placeholder",
        "an effect's trigger with no input of its own"
    )]
    [TestCase(
        "glossary",
        "migration-from-own-version",
        "steps from version 2, and the machine is version 2"
    )]
    [TestCase("glossary", "migration-missing", "has none from version 1")]
    [TestCase("glossary", "owned-by-one-not-an-actor", "'visitor' is not an actor")]
    [TestCase(
        "glossary",
        "reduction-without-handler",
        "has no 'checkout:add-item', a custom reduction the machine names"
    )]
    [TestCase("glossary", "rule-nothing-names", "is a custom rule nothing in the machine names")]
    [TestCase("glossary", "second-effect", "is a second effect")]
    [TestCase("glossary", "state-context-field-named-state", "has a field named 'state'")]
    [TestCase("glossary", "state-context-field-twice", "repeats the field 'receipt'")]
    [TestCase(
        "glossary",
        "system-invoke-input-owner",
        "a system machine has no owner, so nothing is $owner"
    )]
    [TestCase(
        "glossary",
        "system-keyed-trigger-without-subject",
        "must say where the ticket and its key go"
    )]
    [TestCase("glossary", "trigger-guard-unknown", "'missing' is not one of the machine's guards")]
    [TestCase(
        "glossary",
        "view-reads-keyed-system-machine",
        "is a keyed system machine, which a view does not read"
    )]
    [TestCase("host", "view-name-not-a-graphql-name", "has no GraphQL name")]
    [TestCase("host", "view-names-fold-together", "is TheShop in GraphQL")]
    [TestCase("host", "view-row-types-collide", "PinnedStickyNoteRow")]
    [TestCase("host", "policy-broadcast-from-user-machine", "starts from a system machine")]
    [TestCase("host", "duplicate-json-key", "version")]
    [TestCase(
        "host",
        "keyed-system-machine-client-trigger",
        "a system machine's triggers are fired by the server only"
    )]
    [TestCase("host", "migration-key-past-int", "is not a version a migration steps from")]
    [TestCase(
        "host",
        "policy-from-keyed-system-machine",
        "is a keyed system machine, which sets off no policy"
    )]
    [TestCase("host", "system-machine-effect", "a system machine runs no effect")]
    [TestCase(
        "host",
        "system-machine-invoke-owner",
        "a system machine has no owner, so nothing is $owner"
    )]
    [TestCase(
        "host",
        "view-reads-keyed-system-machine",
        "is a keyed system machine, which a view does not read"
    )]
    public void A_refused_document_is_refused_for_what_it_is_named(
        string format,
        string name,
        string reason
    )
    {
        var text = File.ReadAllText(
            Path.Combine(FixturePaths.ConformanceRoot, "formats", format, "refused", name + ".json")
        );

        Why(format, text).Should().Contain(reason);
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
