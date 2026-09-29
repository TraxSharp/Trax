using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Middleware;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// The decision persisted-operation enforcement takes for a document the caller sent inline.
/// How a document arrives is covered by <c>PersistedOperationTransportTests</c>; these pin what
/// is decided once it has.
/// </summary>
[TestFixture]
public class PersistedOperationPolicyTests
{
    private static PersistedOperationPolicy Build(Action<PersistedOperationsBuilder> configure)
    {
        var builder = new PersistedOperationsBuilder().UseDatabase("Host=fake;Database=fake");
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

    [Test]
    public void InlineDocument_RequirePersisted_IsRejected()
    {
        Decide(Build(b => b.RequirePersisted(true)), "{ user { id } }")
            .Should()
            .Be(PersistedOperationDecision.Reject);
    }

    [Test]
    public void InlineDocument_ShadowMode_IsLogged()
    {
        Decide(
                Build(b => b.RequirePersisted(false).LogNonPersistedRequests(true)),
                "{ user { id } }"
            )
            .Should()
            .Be(PersistedOperationDecision.Log);
    }

    [Test]
    public void InlineDocument_AllowlistedByName_PassesThrough()
    {
        Decide(
                Build(b => b.RequirePersisted(true).AllowOperations("DevExplore")),
                "query DevExplore { user { id } }",
                operationName: "DevExplore"
            )
            .Should()
            .Be(PersistedOperationDecision.PassThrough);
    }

    [Test]
    public void InlineDocument_AllowlistedByPredicate_PassesThrough()
    {
        Decide(
                Build(b =>
                    b.RequirePersisted(true).AllowOperationsMatching(s => s.StartsWith("dev_"))
                ),
                "query dev_something { user { id } }",
                operationName: "dev_something"
            )
            .Should()
            .Be(PersistedOperationDecision.PassThrough);
    }

    [Test]
    public void InlineDocument_AllowlistedByDocumentId_WhenNoNameIsGiven_PassesThrough()
    {
        Decide(
                Build(b => b.RequirePersisted(true).AllowOperations("dev_doc")),
                "{ user { id } }",
                documentId: "dev_doc"
            )
            .Should()
            .Be(PersistedOperationDecision.PassThrough);
    }

    [Test]
    public void ManagementMutation_PassesThrough()
    {
        // Persisting the upload mutation by id would be a chicken-and-egg.
        Decide(
                Build(b => b.RequirePersisted(true)),
                """
                mutation {
                  operations {
                    persistedOperations {
                      uploadPersistedOperation(input: { id: "x", document: "{ x }" }) { success }
                    }
                  }
                }
                """
            )
            .Should()
            .Be(PersistedOperationDecision.PassThrough);
    }

    [Test]
    public void ManagementQuery_PassesThrough()
    {
        Decide(
                Build(b => b.RequirePersisted(true)),
                "query { operations { persistedOperations { persistedOperations { totalCount } } } }"
            )
            .Should()
            .Be(PersistedOperationDecision.PassThrough);
    }

    [Test]
    public void ManagementFieldNamedInAnArgument_IsRejected()
    {
        Decide(
                Build(b => b.RequirePersisted(true)),
                """query { user(name: "persistedOperations") { id } }"""
            )
            .Should()
            .Be(
                PersistedOperationDecision.Reject,
                "the name inside a string argument is data, not a selection"
            );
    }

    [Test]
    public void FieldAliasedToTheManagementName_IsRejected()
    {
        Decide(
                Build(b => b.RequirePersisted(true)),
                "query { persistedOperations: __typename user { id } }"
            )
            .Should()
            .Be(PersistedOperationDecision.Reject, "an alias is a response key, not a selection");
    }

    [Test]
    public void ManagementMixedWithAnotherNamespace_IsRejected()
    {
        Decide(
                Build(b => b.RequirePersisted(true)),
                """
                mutation {
                  operations {
                    persistedOperations {
                      uploadPersistedOperation(input: { id: "x", document: "{ x }" }) { success }
                    }
                    deadLetters { totalCount }
                  }
                }
                """
            )
            .Should()
            .Be(PersistedOperationDecision.Reject);
    }

    [Test]
    public void DocumentThatDoesNotParse_IsRejected()
    {
        Decide(Build(b => b.RequirePersisted(true)), "query { operations { persistedOperations {")
            .Should()
            .Be(PersistedOperationDecision.Reject);
    }

    [Test]
    public void Introspection_PassesThroughByDefault()
    {
        Decide(
                Build(b => b.RequirePersisted(true)),
                "query IntrospectionQuery { __schema { types { name } } }",
                operationName: "IntrospectionQuery"
            )
            .Should()
            .Be(PersistedOperationDecision.PassThrough);
    }

    [Test]
    public void UnnamedIntrospection_PassesThroughByDefault()
    {
        Decide(Build(b => b.RequirePersisted(true)), "{ __schema { types { name } } }")
            .Should()
            .Be(PersistedOperationDecision.PassThrough);
    }

    [Test]
    public void DocumentNamedIntrospectionQuery_SelectingData_IsRejected()
    {
        Decide(
                Build(b => b.RequirePersisted(true)),
                "query IntrospectionQuery { users { id } }",
                operationName: "IntrospectionQuery"
            )
            .Should()
            .Be(PersistedOperationDecision.Reject);
    }

    [Test]
    public void Introspection_WithDisableIntrospection_IsRejected()
    {
        Decide(
                Build(b => b.RequirePersisted(true).DisableIntrospection()),
                "{ __schema { types { name } } }"
            )
            .Should()
            .Be(PersistedOperationDecision.Reject);
    }
}
