using Trax.Samples.Auth.E2E.Fixtures;

namespace Trax.Samples.Auth.E2E.Tests;

/// <summary>
/// Every GraphQL request lands in the audit trail with the principal that made it, refused ones
/// included, and without the values the caller sent. Auditors read it back over GraphQL.
/// </summary>
[TestFixture]
public class AuditTrailTests : AuthTestFixture
{
    [Test]
    public async Task EachCall_IsRecordedWithItsSchemeQualifiedPrincipal()
    {
        var calls = new[]
        {
            (AliceKey, "TraxApiKey:alice", (string?)"apikey"),
            (AliceToken, "TraxJwt:alice", "jwt"),
            (Anonymous, "<anonymous>", null),
        };

        foreach (var (caller, principalId, principalType) in calls)
        {
            var operation = UniqueOperation("Echo");
            var result = await GraphQL.SendAsync(
                $$"""query {{operation}} { discover { echo(input: { message: "hi" }) { echoed } } }""",
                caller,
                operation
            );
            result.HasErrors.Should().BeFalse($"{caller}: {result.Raw}");

            var row = (await AuditFor(operation)).Single();
            row.PrincipalId.Should().Be(principalId, $"the audit row names {caller}");
            row.PrincipalType.Should().Be(principalType);
            row.Success.Should().BeTrue();
        }
    }

    [Test]
    public async Task ARefusedCall_IsRecordedAsUnsuccessful_WithTheCaller()
    {
        var operation = UniqueOperation("Publish");
        var result = await GraphQL.SendAsync(
            $$"""mutation {{operation}} { dispatch { news { publishArticle(input: { title: "t", body: "b" }) { output { articleId } } } } }""",
            ErinToken,
            operation
        );
        result.IsRefused.Should().BeTrue(result.Raw);

        var row = (await AuditFor(operation)).Single();
        row.PrincipalId.Should().Be("TraxJwt:erin");
        row.Success.Should().BeFalse();
        row.ErrorText.Should().Be("TRAX_AUTHORIZATION at dispatch.news.publishArticle");
    }

    [Test]
    public async Task ARefusedOperationsCall_IsRecorded()
    {
        var operation = UniqueOperation("Health");
        var result = await GraphQL.SendAsync(
            $"query {operation} {{ operations {{ health {{ status }} }} }}",
            BobKey,
            operation
        );
        result.IsRefused.Should().BeTrue(result.Raw);

        var row = (await AuditFor(operation)).Single();
        row.PrincipalId.Should().Be("TraxApiKey:bob");
        row.Success.Should().BeFalse();
        row.ErrorText.Should().Be("TRAX_AUTHORIZATION at operations");
    }

    [Test]
    public async Task TheRecord_KeepsTheShapeOfTheCall_NotTheValues()
    {
        var operation = UniqueOperation("Publish");
        var secret = $"embargoed-{Guid.NewGuid():N}";
        var result = await GraphQL.SendAsync(
            $$"""mutation {{operation}} { dispatch { news { publishArticle(input: { title: "{{secret}}", body: "b" }) { output { articleId } } } } }""",
            AliceKey,
            operation
        );
        result.HasErrors.Should().BeFalse(result.Raw);

        var row = (await AuditFor(operation)).Single();
        row.Document.Should().Contain("publishArticle").And.Contain("title: \"\"");
        row.Document.Should().NotContain(secret, "string literals are blanked before recording");
    }

    [Test]
    public async Task AnAuditor_ReadsTheTrailOverGraphQL()
    {
        var operation = UniqueOperation("WhoAmI");
        await GraphQL.SendAsync(
            $"query {operation} {{ discover {{ whoAmI {{ id }} }} }}",
            BobToken,
            operation
        );
        await AuditFor(operation);

        var result = await GraphQL.SendAsync(
            $$"""{ discover { audit { auditRecords(where: { operationName: { eq: "{{operation}}" } }) { nodes { principalId principalType success } } } } }""",
            OscarToken
        );

        result.HasErrors.Should().BeFalse(result.Raw);
        var node = result.GetData("discover", "audit", "auditRecords", "nodes")[0];
        node.GetProperty("principalId").GetString().Should().Be("TraxJwt:bob");
        node.GetProperty("principalType").GetString().Should().Be("jwt");
        node.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
