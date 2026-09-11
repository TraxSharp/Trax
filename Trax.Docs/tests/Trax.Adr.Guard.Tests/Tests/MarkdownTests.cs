namespace Trax.Adr.Guard.Tests.Tests;

/// <summary>
/// Reading markdown is where the checks are most easily fooled, because an ADR's most
/// natural content is a quoted example of the very structure being checked.
/// </summary>
[TestFixture]
public class MarkdownTests
{
    #region Fenced headings

    /// <summary>
    /// The false pass this helper exists to prevent: an ADR quoting the format template
    /// carries a fenced <c>## Exemplars</c>, and reading that as the real section let a
    /// document whose actual Exemplars section was silent satisfy the check.
    /// </summary>
    [Test]
    public void Section_HeadingInsideAFence_IsNotMistakenForTheRealOne()
    {
        const string text = """
            # A decision

            The template looks like this:

            ```md
            ## Exemplars

            **Unenforced:** the quoted example, which is not this document's answer.
            ```

            ## Exemplars

            The real content.
            """;

        Markdown.Section(text, "Exemplars").Should().Be("The real content.");
    }

    [Test]
    public void Section_FencedHeadingOnly_ResolvesToNull_NotToTheExample()
    {
        const string text = """
            # A decision

            ```md
            ## Exemplars

            **Unenforced:** quoted, not declared.
            ```
            """;

        Markdown.Section(text, "Exemplars").Should().BeNull();
    }

    [Test]
    public void Section_BodyContainingAFencedHeading_DoesNotEndEarly()
    {
        const string text = """
            ## Status

            **Accepted.**

            ```md
            ## Changelog
            ```

            Still the status section.

            ## Changelog

            - **2026-09-11**: Recorded.
            """;

        var section = Markdown.Section(text, "Status");

        section.Should().Contain("Still the status section");
        section.Should().NotContain("- **2026-09-11**");
    }

    [Test]
    public void WithoutFences_DropsFencedContent_AndKeepsProse()
    {
        const string text = "prose one\n```\n`FakeTests`\n```\nprose two";

        Markdown.WithoutFences(text).Should().Be("prose one\nprose two");
    }

    #endregion

    #region Table rows

    private const string Table = """
        | Area | ADRs |
        | --- | --- |
        | `testing` | [0001](./0001-a-thing.md) |
        """;

    [Test]
    public void TableRows_SkipsTheHeaderRow_NotJustTheSeparator()
    {
        var rows = Markdown.TableRows(Table, 2).ToList();

        rows.Should().ContainSingle("only the data row is data");
        rows[0][0].Trim().Should().Be("`testing`");
    }

    [Test]
    public void TableRows_WithALinkInTheHeader_StillSkipsIt()
    {
        var table = Table.Replace("| Area | ADRs |", "| Area | ADRs [0009](./0009-x.md) |");

        Markdown.TableRows(table, 2).Should().ContainSingle();
    }

    [Test]
    public void TableRows_EscapedPipeInACell_DoesNotShiftTheColumns()
    {
        const string table = """
            | Area | ADRs |
            | --- | --- |
            | `a \| b` | [0001](./0001-a-thing.md) |
            """;

        var cells = Markdown.TableRows(table, 2).Single();

        cells[0].Trim().Should().Be("`a | b`", "the escape is unescaped, not split on");
        cells[1].Should().Contain("0001-a-thing.md", "the link stays in the ADRs column");
    }

    [Test]
    public void TableRows_IgnoresTablesInsideFences()
    {
        var table = "```md\n" + Table + "\n```\n";

        Markdown.TableRows(table, 2).Should().BeEmpty();
    }

    #endregion
}
