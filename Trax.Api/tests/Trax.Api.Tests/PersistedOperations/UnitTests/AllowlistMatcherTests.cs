using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Middleware;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

[TestFixture]
public class AllowlistMatcherTests
{
    private static AllowlistMatcher Build(
        IEnumerable<string>? names = null,
        IEnumerable<Func<string, bool>>? predicates = null
    ) =>
        new(
            new PersistedOperationsOptions
            {
                AllowedOperationNames = new HashSet<string>(
                    names ?? Array.Empty<string>(),
                    StringComparer.Ordinal
                ),
                AllowOperationPredicates = (
                    predicates ?? Array.Empty<Func<string, bool>>()
                ).ToList(),
            }
        );

    [Test]
    public void IsAllowed_ExactNameMatch_ReturnsTrue() =>
        Build(names: new[] { "ListUsers" })
            .IsAllowed("ListUsers", documentId: null)
            .Should()
            .BeTrue();

    [Test]
    public void IsAllowed_NameMismatch_ReturnsFalse() =>
        Build(names: new[] { "ListUsers" })
            .IsAllowed("ListPosts", documentId: null)
            .Should()
            .BeFalse();

    [Test]
    public void IsAllowed_NameComparisonIsCaseSensitive() =>
        Build(names: new[] { "ListUsers" })
            .IsAllowed("listusers", documentId: null)
            .Should()
            .BeFalse();

    [Test]
    public void IsAllowed_PredicateMatch_ReturnsTrue() =>
        Build(predicates: new Func<string, bool>[] { id => id.StartsWith("dev_") })
            .IsAllowed("dev_explore", documentId: null)
            .Should()
            .BeTrue();

    [Test]
    public void IsAllowed_PredicateNoMatch_ReturnsFalse() =>
        Build(predicates: new Func<string, bool>[] { id => id.StartsWith("dev_") })
            .IsAllowed("prod_explore", documentId: null)
            .Should()
            .BeFalse();

    [Test]
    public void IsAllowed_FallsBackToDocumentId_WhenOperationNameNull() =>
        Build(names: new[] { "userProfile_v1" })
            .IsAllowed(operationName: null, documentId: "userProfile_v1")
            .Should()
            .BeTrue();

    [Test]
    public void IsAllowed_BothNullKeys_ReturnsFalse() =>
        Build(names: new[] { "ListUsers" })
            .IsAllowed(operationName: null, documentId: null)
            .Should()
            .BeFalse();

    [Test]
    public void IsAllowed_NoConfiguredAllowlist_ReturnsFalse() =>
        Build().IsAllowed("AnyName", documentId: null).Should().BeFalse();

    [Test]
    public void IsAllowed_MultiplePredicates_FirstMatchWins()
    {
        var calls = new List<int>();
        var matcher = Build(
            predicates: new Func<string, bool>[]
            {
                _ =>
                {
                    calls.Add(1);
                    return false;
                },
                _ =>
                {
                    calls.Add(2);
                    return true;
                },
                _ =>
                {
                    calls.Add(3);
                    return true;
                },
            }
        );

        matcher.IsAllowed("anything", documentId: null).Should().BeTrue();
        calls.Should().Equal(1, 2);
    }

    [Test]
    public void Constructor_NullOptions_Throws()
    {
        Action act = () => _ = new AllowlistMatcher(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
