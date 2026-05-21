using FluentAssertions;
using HotChocolate;
using HotChocolate.AspNetCore.Authorization;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;
using Trax.Api.GraphQL.PersistedOperations.Storage.Validation;

namespace Trax.Api.Tests.PersistedOperations.UnitTests.Validation;

/// <summary>
/// Unit coverage for <see cref="HotChocolateSchemaValidator"/>. The validator
/// is exercised directly against a hand-built HotChocolate schema so the
/// tests stay independent of Trax's GraphQL/Postgres composition. Integration
/// coverage (DB + UpsertAsync) lives in
/// <see cref="IntegrationTests.PersistedOperationMutationTests"/>.
/// </summary>
[TestFixture]
public class HotChocolateSchemaValidatorTests
{
    /// <summary>
    /// Regression for the MissingStateException that bit any caller of
    /// UpsertAsync after Trax started auto-wiring @authorize. The validator
    /// bypasses the request pipeline (and therefore
    /// AuthorizationContextEnricher), so it has to seed the authorization
    /// handler into the validator's contextData itself.
    /// </summary>
    [Test]
    public async Task ValidateAsync_SchemaWithAuthorizeDirective_DoesNotThrowMissingStateException()
    {
        await using var sp = BuildSchemaProvider(addAuthorization: true);
        var validator = new HotChocolateSchemaValidator(sp);

        // hello has @authorize on the schema; this document is otherwise valid.
        var act = () => validator.ValidateAsync("query Q { hello }", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ValidateAsync_SchemaWithAuthorize_InvalidField_ReportsValidationFailure()
    {
        await using var sp = BuildSchemaProvider(addAuthorization: true);
        var validator = new HotChocolateSchemaValidator(sp);

        var act = () => validator.ValidateAsync("query Q { ghost }", CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<PersistedOperationValidationException>()).Which;
        ex.Failures.Should().NotBeEmpty();
    }

    [Test]
    public async Task ValidateAsync_SchemaWithoutAuthorization_ValidDocument_Succeeds()
    {
        // Hosts that never wire @authorize must keep working: the validator
        // gracefully resolves a missing IAuthorizationHandler.
        await using var sp = BuildSchemaProvider(addAuthorization: false);
        var validator = new HotChocolateSchemaValidator(sp);

        var act = () => validator.ValidateAsync("query Q { hello }", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ValidateAsync_SyntaxError_ThrowsParseException()
    {
        await using var sp = BuildSchemaProvider(addAuthorization: true);
        var validator = new HotChocolateSchemaValidator(sp);

        var act = () => validator.ValidateAsync("query Q { hello", CancellationToken.None);

        await act.Should().ThrowAsync<PersistedOperationParseException>();
    }

    [Test]
    public async Task ValidateAsync_CalledTwice_ReusesCachedValidator()
    {
        // The validator caches the per-schema executor + IDocumentValidator
        // on first use. Hitting the cache on the second call has to keep
        // working with the @authorize handler-injection path; this case
        // exercises both the cache-miss and cache-hit branches.
        await using var sp = BuildSchemaProvider(addAuthorization: true);
        var validator = new HotChocolateSchemaValidator(sp);

        await validator.ValidateAsync("query Q { hello }", CancellationToken.None);
        var act = () => validator.ValidateAsync("query R { hello }", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ValidateAsync_PreCancelledToken_Throws()
    {
        await using var sp = BuildSchemaProvider(addAuthorization: true);
        var validator = new HotChocolateSchemaValidator(sp);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => validator.ValidateAsync("query Q { hello }", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ServiceProvider BuildSchemaProvider(bool addAuthorization)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();

        var graphql = sc.AddGraphQLServer("trax").AddQueryType<HelloQuery>();

        if (addAuthorization)
        {
            // Triggers the @authorize directive registration in the schema AND
            // wires DefaultAuthorizationHandler as the IAuthorizationHandler.
            // The DefaultAuthorizationHandler depends on ASP.NET Core's
            // IAuthorizationService, which AddAuthorization() also registers.
            graphql.AddAuthorization();
        }

        return sc.BuildServiceProvider();
    }

    public class HelloQuery
    {
        [Authorize]
        public string Hello() => "world";
    }
}
