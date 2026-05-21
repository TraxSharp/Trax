using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Edge paths in the typed-query generator that the main matrix doesn't cover. Each test
/// catches a specific regression in the property->selection walk: misnamed fields,
/// not-skipped JsonIgnore, operation-name overrides not honored.
/// </summary>
[TestFixture]
public class TypedQueryGeneratorEdgePathsTests
{
    private GraphQLTestServerFixture _fixture = null!;
    private ServiceProvider _services = null!;
    private IGraphQLClientExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new GraphQLTestServerFixture();
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(_fixture.BaseAddress)
            .ConfigureHttpClient(_fixture.CreateHttpClient());
        _services = services.BuildServiceProvider();
        _executor = _services.GetRequiredService<IGraphQLClientExecutor>();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _fixture.Dispose();
    }

    [Test]
    public void GraphQLField_OverridesPropertyNameInQuery()
    {
        var request = new FieldRenamedRequest { Id = "x" };

        var query = request.Query;

        // The C# property is DisplayName but the query must select `name` because of
        // [GraphQLField("name")]. Without this test, a regression that ignored the attribute
        // would produce a query selecting `displayName` which the server doesn't have, and
        // the failure would be a server-side validation error rather than a clear local
        // generation bug.
        query.Should().Contain("name");
        query.Should().NotContain("displayName");
    }

    [Test]
    public void JsonIgnore_OmitsPropertyFromSelection()
    {
        var request = new FieldRenamedRequest { Id = "x" };

        var query = request.Query;

        // InternalNote is [JsonIgnore]'d - it must not appear in the selection set.
        // A regression here would request a field the server might not have, breaking
        // validation, or worse, requesting one it does have and shipping unexpected data.
        query.Should().NotContain("internalNote");
        query.Should().NotContain("InternalNote");
    }

    [Test]
    public void GraphQLOperation_NameOverride_IsUsedInOperationLine()
    {
        var request = new FieldRenamedRequest { Id = "x" };

        var query = request.Query;

        // [GraphQLOperation(Name = "GetPlayerCustomOp")] overrides the default "FieldRenamed"
        // derived from the type name.
        query.Should().Contain("query GetPlayerCustomOp");
        query.Should().NotContain("query FieldRenamed");
    }

    [Test]
    public async Task GraphQLField_ExecutesAgainstServerSuccessfully()
    {
        // End-to-end: the renamed-field request actually works against a real server.
        // Pin down that the generator's output is server-compatible, not just textually correct.
        var result = await _executor.Run(new FieldRenamedRequest { Id = "player-1" });

        result.Id.Should().Be("player-1");
        result.DisplayName.Should().Be("Aragorn");
        result.Level.Should().Be(42);
        result.InternalNote.Should().BeNull("the field was JsonIgnored and never requested");
    }

    #region Path (nested envelope)

    [Test]
    public void Path_TwoSegments_WrapsRootFieldInNestedBraces()
    {
        var request = new GetNestedCustomerByEmailRequest { Email = "ignored" };

        var query = request.Query;

        // Brace order: discover { netsuite { typedCustomer(...) { ... } } }
        query.Should().Contain("discover {");
        query.Should().Contain("netsuite {");
        query.Should().Contain("typedCustomer(email: $email)");

        // discover precedes netsuite precedes typedCustomer in the stream.
        var iDiscover = query.IndexOf("discover {", StringComparison.Ordinal);
        var iNetsuite = query.IndexOf("netsuite {", StringComparison.Ordinal);
        var iRoot = query.IndexOf("typedCustomer(", StringComparison.Ordinal);
        iDiscover.Should().BeLessThan(iNetsuite);
        iNetsuite.Should().BeLessThan(iRoot);
    }

    [Test]
    public void Path_BraceCount_IsBalanced()
    {
        // A nested wrapper that fails to close its braces would still pass the contains-checks
        // above, but the server would reject it with a syntax error. Counting braces catches
        // off-by-one indentation/close-brace regressions without needing a server roundtrip.
        var request = new GetNestedCustomerByEmailRequest { Email = "ignored" };

        var query = request.Query;
        var open = query.Count(c => c == '{');
        var close = query.Count(c => c == '}');

        open.Should().Be(close, "every opening brace must have a matching close");
        open.Should()
            .BeGreaterThan(2, "operation + 2 path levels + root selection = at least 4 braces");
    }

    [Test]
    public void Path_ArgumentsAttachToRootField_NotWrapper()
    {
        var request = new GetNestedCustomerByEmailRequest { Email = "x" };

        var query = request.Query;

        // The $email variable must be declared on the operation signature, and consumed
        // on the innermost root field, NOT on the discover/netsuite wrapper fields.
        query.Should().Contain("query GetNestedCustomerByEmail($email: String!)");
        query.Should().Contain("typedCustomer(email: $email)");

        // Wrapper fields must be argumentless.
        query.Should().NotContain("discover(");
        query.Should().NotContain("netsuite(");
    }

    [Test]
    public void Path_ArgumentlessRootField_StillWrapsCorrectly()
    {
        var request = new GetNestedCustomersRequest();

        var query = request.Query;

        query.Should().Contain("query GetNestedCustomers");
        query.Should().Contain("discover {");
        query.Should().Contain("netsuite {");
        query.Should().Contain("typedCustomers {");

        // No argument signature on the operation.
        query.Should().NotContain("$");
    }

    [Test]
    public void Path_SingleSegment_WrapsOnce()
    {
        var request = new SingleLevelPathRequest();

        var query = request.Query;

        query.Should().Contain("discover {");
        query.Should().Contain("netsuite {");

        // Only one wrapper above the root field. The root field IS netsuite here, projecting
        // the namespace shape itself. So we expect exactly two opening braces of nested fields
        // inside the operation body: one for discover (path wrapper) and one for netsuite (root field).
        query.Should().NotContain("players {");
    }

    [Test]
    public void Path_NullOrUnset_BehavesLikeFlat()
    {
        // Regression: existing flat requests must produce identical output to before the
        // Path feature landed. The generator's null-path branch must not introduce a wrapping
        // level when Path is unset.
        var flat = new GetPlayerByTypedRequest { Id = "x" };

        var query = flat.Query;

        query.Should().Contain("query GetPlayerByTyped");
        query.Should().Contain("player(id: $id)");
        query.Should().NotContain("discover");
    }

    [Test]
    public void Path_EmptyString_Throws()
    {
        // Empty string is operator error: setting the property at all means you mean to nest.
        // The null check is the documented way to opt out.
        var act = () =>
            TypedQueryGenerator.Generate(typeof(EmptyPathRequest), typeof(TypedItemSummary));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Path*EmptyPathRequest*");
    }

    [Test]
    public void Path_LeadingDot_Throws()
    {
        var act = () =>
            TypedQueryGenerator.Generate(typeof(LeadingDotPathRequest), typeof(TypedItemSummary));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Path*LeadingDotPathRequest*");
    }

    [Test]
    public void Path_TrailingDot_Throws()
    {
        var act = () =>
            TypedQueryGenerator.Generate(typeof(TrailingDotPathRequest), typeof(TypedItemSummary));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Path*TrailingDotPathRequest*");
    }

    [Test]
    public void Path_DoubleDot_Throws()
    {
        var act = () =>
            TypedQueryGenerator.Generate(typeof(DoubleDotPathRequest), typeof(TypedItemSummary));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Path*DoubleDotPathRequest*");
    }

    [Test]
    public void Path_WhitespaceSegment_Throws()
    {
        var act = () =>
            TypedQueryGenerator.Generate(
                typeof(WhitespaceSegmentPathRequest),
                typeof(TypedItemSummary)
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Path*WhitespaceSegmentPathRequest*");
    }

    [GraphQLOperation(OperationType.Query, Path = "", RootField = "typedCustomers")]
    private sealed class EmptyPathRequest : TypedRequest<IReadOnlyList<TypedItemSummary>> { }

    [GraphQLOperation(OperationType.Query, Path = ".discover", RootField = "typedCustomers")]
    private sealed class LeadingDotPathRequest : TypedRequest<IReadOnlyList<TypedItemSummary>> { }

    [GraphQLOperation(OperationType.Query, Path = "discover.", RootField = "typedCustomers")]
    private sealed class TrailingDotPathRequest : TypedRequest<IReadOnlyList<TypedItemSummary>> { }

    [GraphQLOperation(
        OperationType.Query,
        Path = "discover..netsuite",
        RootField = "typedCustomers"
    )]
    private sealed class DoubleDotPathRequest : TypedRequest<IReadOnlyList<TypedItemSummary>> { }

    [GraphQLOperation(
        OperationType.Query,
        Path = "discover. netsuite",
        RootField = "typedCustomers"
    )]
    private sealed class WhitespaceSegmentPathRequest
        : TypedRequest<IReadOnlyList<TypedItemSummary>> { }

    #endregion
}
