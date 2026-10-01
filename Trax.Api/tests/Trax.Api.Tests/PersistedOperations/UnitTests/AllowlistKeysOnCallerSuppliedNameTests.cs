using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Middleware;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// The persisted-operations allowlist decides on the operation name, or the document id, that the
/// caller supplies in the request. It never inspects what the document selects, so an
/// allowlisted name admits whatever document carries it.
///
/// <para>Enforces <c>docs/adr/0005-the-allowlist-keys-on-the-caller-supplied-operation-name.md</c>:
/// that is a documented convenience for trusted networks, not a control. A change that makes the
/// allowlist look at the document is a change to that decision and should supersede it, not slip
/// in underneath the documentation that describes it.</para>
/// </summary>
[Property("adr", "docs/adr/0005-the-allowlist-keys-on-the-caller-supplied-operation-name.md")]
[TestFixture]
public class AllowlistKeysOnCallerSuppliedNameTests
{
    private const string Adr =
        "docs/adr/0005-the-allowlist-keys-on-the-caller-supplied-operation-name.md";

    private const string UnrelatedDocument = "query HealthProbe { users { id email } }";

    [Test]
    public void AllowOperations_AdmitsAnyDocumentCarryingTheName()
    {
        var policy = Build(b => b.RequirePersisted(true).AllowOperations("HealthProbe"));

        Decide(policy, UnrelatedDocument, operationName: "HealthProbe")
            .Should()
            .Be(
                PersistedOperationDecision.PassThrough,
                "the allowlist matches the caller-supplied operationName and does not inspect "
                    + "the document, per "
                    + Adr
            );
    }

    [Test]
    public void AllowOperations_SameDocumentWithoutTheName_IsRejected()
    {
        var policy = Build(b => b.RequirePersisted(true).AllowOperations("HealthProbe"));

        Decide(policy, UnrelatedDocument, operationName: null)
            .Should()
            .Be(
                PersistedOperationDecision.Reject,
                "the operation name inside the document is not the key; only the request's "
                    + "operationName is, per "
                    + Adr
            );
    }

    [Test]
    public void AllowOperationsMatching_SeesTheCallerSuppliedNameOnly()
    {
        var seen = new List<string>();
        var policy = Build(b =>
            b.RequirePersisted(true)
                .AllowOperationsMatching(key =>
                {
                    seen.Add(key);
                    return key.StartsWith("dev_", StringComparison.Ordinal);
                })
        );

        Decide(policy, "query dev_anything { users { id } }", operationName: "dev_x")
            .Should()
            .Be(PersistedOperationDecision.PassThrough);
        seen.Should()
            .Equal(
                new[] { "dev_x" },
                "the predicate is handed the request's operationName, never the document, per "
                    + Adr
            );
    }

    [Test]
    public void UnnamedRequest_IsMatchedOnTheCallerSuppliedDocumentId()
    {
        var policy = Build(b => b.RequirePersisted(true).AllowOperations("probe_v1"));

        Decide(policy, "{ users { id } }", documentId: "probe_v1")
            .Should()
            .Be(
                PersistedOperationDecision.PassThrough,
                "an unnamed request falls back to the caller-supplied document id, per " + Adr
            );
    }

    private static PersistedOperationPolicy Build(Action<PersistedOperationsBuilder> configure)
    {
        var builder = new PersistedOperationsBuilder()
            .UseDatabase("Host=fake;Database=fake")
            .SingleNode();
        configure(builder);
        var options = builder.Build();
        return new PersistedOperationPolicy(options, new AllowlistMatcher(options));
    }

    private static PersistedOperationDecision Decide(
        PersistedOperationPolicy policy,
        string query,
        string? operationName = null,
        string? documentId = null
    ) => policy.Decide(operationName, documentId, GraphQLDocumentParser.TryParse(query));
}
