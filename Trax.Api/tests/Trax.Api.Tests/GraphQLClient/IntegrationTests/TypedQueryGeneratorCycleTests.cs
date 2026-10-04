using AwesomeAssertions;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// A GraphQL selection is explicit and finite, so a result type that reaches itself through
/// its own properties has no query to generate. The generator refuses it with an exception
/// naming the path, which a host's startup validation reports, instead of recursing forever.
/// </summary>
[TestFixture]
public class TypedQueryGeneratorCycleTests
{
    [Test]
    public void A_self_referential_result_type_is_refused_with_the_cycle_named()
    {
        var act = () => new GetCategoryRequest { Id = "c1" }.Query;

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SelfRefCategory.Parent -> SelfRefCategory*");
    }

    [Test]
    public void A_cycle_through_a_list_and_a_second_type_is_refused()
    {
        var act = () => new GetFolderRequest().Query;

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*CycleFolder.Files -> CycleFile.Folder -> CycleFolder*");
    }

    [Test]
    public void The_same_type_under_two_sibling_properties_is_not_a_cycle()
    {
        var query = new GetMatchRequest().Query;

        query.Should().Contain("home {").And.Contain("away {");
    }
}

[GraphQLType("Category")]
public sealed record SelfRefCategory(string Id, string Name, SelfRefCategory? Parent);

[GraphQLOperation(OperationType.Query, RootField = "category")]
public sealed class GetCategoryRequest : TypedRequest<SelfRefCategory>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}

[GraphQLType("Folder")]
public sealed record CycleFolder(string Id, IReadOnlyList<CycleFile> Files);

[GraphQLType("File")]
public sealed record CycleFile(string Id, CycleFolder Folder);

[GraphQLOperation(OperationType.Query, RootField = "folder")]
public sealed class GetFolderRequest : TypedRequest<CycleFolder>;

[GraphQLType("Team")]
public sealed record MatchTeam(string Id, string Name);

[GraphQLType("Match")]
public sealed record SiblingMatch(string Id, MatchTeam Home, MatchTeam Away);

[GraphQLOperation(OperationType.Query, RootField = "match")]
public sealed class GetMatchRequest : TypedRequest<SiblingMatch>;
