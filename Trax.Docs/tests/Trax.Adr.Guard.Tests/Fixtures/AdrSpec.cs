namespace Trax.Adr.Guard.Tests.Fixtures;

/// <summary>One ADR's identity, as both the document and the index row need to agree on it.</summary>
public sealed record AdrSpec(
    string Number,
    string Slug,
    string Title,
    string[] Areas,
    string[] Repos
)
{
    public string FileName => $"{Number}-{Slug}.md";

    public string Link => $"[{Number}](./{FileName})";
}
