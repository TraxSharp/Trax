using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// <c>[TraxAuthorize]</c> and <c>[TraxAllowAnonymous]</c> on a resolver, end to end over HTTP.
/// The census refuses a field that declares nothing; these prove the other half, that a field
/// which does declare is actually enforced rather than merely annotated.
///
/// <para>Enforces <c>docs/adr/0003-a-type-extension-field-declares-its-own-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md")]
[TestFixture]
public class ResolverAuthorizationTests
{
    private const string Adr = "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md";

    // ── [TraxAuthorize] on a resolver is a real gate ─────────────────────

    /// <summary>
    /// The whole point of owning the vocabulary: Trax reads its own attribute off the resolver
    /// and emits the server's directive, so the field is enforced, not merely annotated.
    /// </summary>
    [Test]
    public async Task TraxAuthorizeOnAResolver_RefusesAnAnonymousCaller()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGated
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            query: AdminOperationsAuthorizationTests.GatedQuery
        );

        AdminOperationsAuthorizationTests
            .HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue(
                "[TraxAuthorize] on a resolver method emits @authorize on that field, per " + Adr
            );

        await host.StopAsync();
    }

    [Test]
    public async Task TraxAuthorizeOnAResolver_RefusesAnAuthenticatedCallerWithoutTheRole()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGated
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: AdminOperationsAuthorizationTests.ReaderApiKey,
            query: AdminOperationsAuthorizationTests.GatedQuery
        );

        AdminOperationsAuthorizationTests
            .HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue("Roles on the attribute reach the directive, not just the census");

        await host.StopAsync();
    }

    [Test]
    public async Task TraxAuthorizeOnAResolver_ServesTheRoleHolder()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGated
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: AdminOperationsAuthorizationTests.AdminApiKey,
            query: AdminOperationsAuthorizationTests.GatedQuery
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
        doc.RootElement.GetProperty("data")
            .GetProperty("gatedPing")
            .GetString()
            .Should()
            .Be("pong");

        await host.StopAsync();
    }

    /// <summary>
    /// A bare [TraxAuthorize] with no policy and no roles asks only for an authenticated caller,
    /// the same as it does on a train or an entity.
    /// </summary>
    [Test]
    public async Task BareTraxAuthorizeOnAResolver_AsksOnlyForAuthentication()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGated
        );

        var anonymous = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            query: AdminOperationsAuthorizationTests.AuthenticatedQuery
        );
        AdminOperationsAuthorizationTests
            .HasErrorCode(anonymous, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue();

        var reader = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: AdminOperationsAuthorizationTests.ReaderApiKey,
            query: AdminOperationsAuthorizationTests.AuthenticatedQuery
        );
        reader
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse("any authenticated caller satisfies a bare [TraxAuthorize]");

        await host.StopAsync();
    }

    /// <summary>
    /// The sibling stays open. Gating one field on a root type must not gate the root.
    /// </summary>
    [Test]
    public async Task TraxAllowAnonymousSibling_StaysOpenBesideAGatedField()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGated
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            query: AdminOperationsAuthorizationTests.PublicQuery
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();

        await host.StopAsync();
    }
}
