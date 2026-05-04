using FluentAssertions;
using Trax.Cli.Generator;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class NamingConventionsEdgeTests
{
    [Test]
    public void ToPascalCase_EmptyInput_ReturnsEmpty()
    {
        NamingConventions.ToPascalCase("").Should().Be("");
    }

    [Test]
    public void ToPascalCase_OnlySeparators_ReturnsOriginal()
    {
        // Hits the parts.Count == 0 fallback
        NamingConventions.ToPascalCase("___").Should().Be("___");
    }

    [Test]
    public void ToCamelCase_EmptyInput_ReturnsEmpty()
    {
        NamingConventions.ToCamelCase("").Should().Be("");
    }

    [Test]
    public void ToCamelCase_SingleChar_ReturnsLowercase()
    {
        NamingConventions.ToCamelCase("X").Should().Be("x");
    }

    [Test]
    public void DeriveGroupName_VerbPrefix_StripsAndPluralizes()
    {
        // GetStory -> Stories (verb stripped, consonant-Y => -ies)
        NamingConventions.DeriveGroupName("getStory").Should().Be("Stories");
    }

    [Test]
    public void DeriveGroupName_VerbPrefix_VowelBeforeY_AppendsS()
    {
        // GetDay -> Days (verb stripped, vowel-Y => +s)
        NamingConventions.DeriveGroupName("getDay").Should().Be("Days");
    }

    [Test]
    public void DeriveGroupName_AlreadyPlural_UnchangedAfterPluralize()
    {
        // Ends in s, Pluralize returns as-is
        NamingConventions.DeriveGroupName("listStatuses").Should().Be("Statuses");
    }

    [Test]
    public void DeriveGroupName_NoVerbPrefix_PluralizesFullName()
    {
        // No matching verb prefix → falls through to Pluralize(pascal)
        NamingConventions.DeriveGroupName("ping").Should().Be("Pings");
    }

    [Test]
    public void SimplifySchemaName_EmptyInput_ReturnsEmpty()
    {
        NamingConventions.SimplifySchemaName("").Should().Be("");
    }
}
