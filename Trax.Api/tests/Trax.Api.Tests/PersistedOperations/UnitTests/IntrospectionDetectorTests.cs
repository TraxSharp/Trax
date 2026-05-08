using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Middleware;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

[TestFixture]
public class IntrospectionDetectorTests
{
    [Test]
    public void LooksLikeIntrospectionByName_KnownName_ReturnsTrue() =>
        IntrospectionDetector.LooksLikeIntrospectionByName("IntrospectionQuery").Should().BeTrue();

    [TestCase("introspectionquery")] // case-sensitive
    [TestCase("Introspection")]
    [TestCase("__schema")]
    [TestCase("")]
    [TestCase(null)]
    public void LooksLikeIntrospectionByName_OtherValues_ReturnFalse(string? name) =>
        IntrospectionDetector.LooksLikeIntrospectionByName(name).Should().BeFalse();

    [TestCase("query { __schema { queryType { name } } }")]
    [TestCase("query { __type(name: \"User\") { fields { name } } }")]
    [TestCase("query { __schema { types { name } } __type(name: \"X\") { name } }")]
    [TestCase("query { __typename }")]
    [TestCase("{ __schema { types { name } } }")]
    public void IsPureIntrospection_PureIntrospectionDocs_ReturnTrue(string document) =>
        IntrospectionDetector.IsPureIntrospection(document).Should().BeTrue();

    [TestCase("query { user(id: 1) { name } }")]
    [TestCase("query { __schema { types { name } } user(id: 1) { name } }")]
    [TestCase("mutation { createUser(input: {}) { id } }")]
    [TestCase("subscription { messages { id } }")]
    public void IsPureIntrospection_NonIntrospection_ReturnsFalse(string document) =>
        IntrospectionDetector.IsPureIntrospection(document).Should().BeFalse();

    [Test]
    public void IsPureIntrospection_EmptyDocument_ReturnsFalse() =>
        IntrospectionDetector.IsPureIntrospection(string.Empty).Should().BeFalse();

    [Test]
    public void IsPureIntrospection_MalformedDocument_ReturnsFalse() =>
        IntrospectionDetector.IsPureIntrospection("query { not closed").Should().BeFalse();

    [Test]
    public void IsPureIntrospection_DocumentWithNoOperation_ReturnsFalse() =>
        IntrospectionDetector.IsPureIntrospection("fragment F on User { id }").Should().BeFalse();

    [Test]
    public void IsPureIntrospection_InlineFragmentMixed_ReturnsFalse() =>
        IntrospectionDetector
            .IsPureIntrospection("query { ... on Query { user { id } } }")
            .Should()
            .BeFalse();
}
