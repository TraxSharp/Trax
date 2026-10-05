using System.Text.Json;
using Trax.Samples.Auth.E2E.Fixtures;

namespace Trax.Samples.Auth.E2E.Tests;

/// <summary>
/// Query models carry the same attributes as trains. <c>Article</c> is
/// <c>[TraxAllowAnonymous]</c>; the <c>EditorNote</c> it links to is for editors or auditors (two
/// stacked role attributes, which union); <c>AuditRecord</c> is for auditors. Openness does not
/// cascade from a public parent to a gated child, and a gated model cannot be counted.
/// </summary>
[TestFixture]
public class QueryModelAuthorizationTests : AuthTestFixture
{
    private const string ArticlesWithNotes =
        "{ discover { news { articles { nodes { title editorNote { text } } } } } }";

    private const string FilterByNote =
        """{ discover { news { articles(where: { editorNote: { text: { contains: "legal" } } }) { nodes { title } } } } }""";

    private const string NoteText = "Check the quote with legal";

    [Test]
    public async Task Articles_AreServedToAnAnonymousCaller()
    {
        var result = await GraphQL.SendAsync(
            "{ discover { news { articles { totalCount nodes { title authorId } } } } }",
            Anonymous
        );

        result.HasErrors.Should().BeFalse(result.Raw);
        result
            .GetData("discover", "news", "articles", "totalCount")
            .GetInt32()
            .Should()
            .BeGreaterThanOrEqualTo(2, "the host seeds two articles");
    }

    [Test]
    public async Task WordCount_ExtensionField_IsPublicAndReadsItsParentColumn()
    {
        // Selected alone, the body is not in the projection unless the resolver declares it.
        var result = await GraphQL.SendAsync(
            "{ discover { news { articles { nodes { wordCount } } } } }",
            Anonymous
        );

        result.HasErrors.Should().BeFalse(result.Raw);
        result
            .GetData("discover", "news", "articles", "nodes")
            .EnumerateArray()
            .Select(n => n.GetProperty("wordCount").GetInt32())
            .Should()
            .Contain(c => c > 0);
    }

    [Test]
    public async Task EditorNote_ThroughAPublicArticle_IsRefusedToAnonymousAndReaders()
    {
        foreach (var caller in new[] { Anonymous, BobKey, BobToken })
        {
            var result = await GraphQL.SendAsync(ArticlesWithNotes, caller);

            result.IsRefused.Should().BeTrue($"{caller}: {result.Raw}");
            result.Raw.Should().NotContain(NoteText, $"no note text may reach {caller}");
        }
    }

    [Test]
    public async Task EditorNote_IsServedToAnEditorOrAnAuditor()
    {
        // Two stacked [TraxAuthorize(Roles = ...)] attributes union: either role is enough.
        foreach (var caller in new[] { AliceKey, AliceToken, OscarKey, OscarToken })
        {
            var result = await GraphQL.SendAsync(ArticlesWithNotes, caller);

            result.HasErrors.Should().BeFalse($"{caller}: {result.Raw}");
            result
                .GetData("discover", "news", "articles", "nodes")
                .EnumerateArray()
                .Where(n => n.GetProperty("editorNote").ValueKind != JsonValueKind.Null)
                .Select(n => n.GetProperty("editorNote").GetProperty("text").GetString())
                .Should()
                .Contain(t => t!.StartsWith(NoteText));
        }
    }

    [Test]
    public async Task FilteringThroughTheGatedNote_IsAuthorizedLikeSelectingIt()
    {
        var anonymous = await GraphQL.SendAsync(FilterByNote, Anonymous);
        anonymous.IsRefused.Should().BeTrue(anonymous.Raw);

        var editor = await GraphQL.SendAsync(FilterByNote, AliceKey);
        editor.HasErrors.Should().BeFalse(editor.Raw);
        editor.GetData("discover", "news", "articles", "nodes").GetArrayLength().Should().Be(1);
    }

    [Test]
    public async Task AuditRecords_AreRefusedToEveryoneButAuditors()
    {
        foreach (var caller in new[] { Anonymous, BobKey, AliceKey, AliceToken, ErinToken })
        {
            var result = await GraphQL.SendAsync(
                "{ discover { audit { auditRecords(first: 1) { nodes { principalId } } } } }",
                caller
            );

            result.IsRefused.Should().BeTrue($"{caller}: {result.Raw}");
        }
    }

    [Test]
    public async Task AuditRecords_CannotEvenBeCounted_WithoutTheRole()
    {
        var result = await GraphQL.SendAsync(
            "{ discover { audit { auditRecords { totalCount } } } }",
            AliceKey
        );

        result.IsRefused.Should().BeTrue(result.Raw);
        result.Raw.Should().NotContain("\"totalCount\":");
    }

    [Test]
    public async Task AuditRecords_AreServedToAnAuditor()
    {
        foreach (var caller in new[] { OscarKey, OscarToken })
        {
            var result = await GraphQL.SendAsync(
                "{ discover { audit { auditRecords { totalCount } } } }",
                caller
            );

            result.HasErrors.Should().BeFalse($"{caller}: {result.Raw}");
        }
    }
}
