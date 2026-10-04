using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Role requirements from separate <c>[TraxAuthorize]</c> attributes, or separate
/// <c>GateOperations(roles:)</c> calls, combine with AND, as separate <c>[Authorize]</c> attributes
/// do in ASP.NET Core. Within one attribute, a comma-separated list is any of. A narrower
/// attribute can therefore only narrow: a method-level <c>Support</c> under a class-level
/// <c>Admin</c> requires both, and never lets a <c>Support</c>-only caller through. Guard for
/// <c>docs/adr/0030-separate-authorization-attributes-combine-with-and.md</c>.
/// </summary>
[Property("adr", "docs/adr/0030-separate-authorization-attributes-combine-with-and.md")]
[TestFixture]
public class RoleCombinationTests
{
    private const string Adr =
        "docs/adr/0030-separate-authorization-attributes-combine-with-and.md: each attribute is a "
        + "requirement of its own";

    // ── Class-level and method-level attributes on a type extension ──────

    [Test]
    public async Task ClassAdminAndMethodSupport_RefusesASupportOnlyCaller()
    {
        using var host = await StartAsync(g => g.AddTypeExtension<NarrowedFields>());

        (await host.PostAsync("{ narrowed }", PostureHost.SupportKey))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr)
            .And.NotContain("narrowed-value");
    }

    [Test]
    public async Task ClassAdminAndMethodSupport_RefusesAnAdminOnlyCaller()
    {
        using var host = await StartAsync(g => g.AddTypeExtension<NarrowedFields>());

        (await host.PostAsync("{ narrowed }", PostureHost.AdminKey))
            .Should()
            .Contain("TRAX_AUTHORIZATION")
            .And.NotContain("narrowed-value");
    }

    [Test]
    public async Task ClassAdminAndMethodSupport_ServesACallerWithBothRoles()
    {
        using var host = await StartAsync(g => g.AddTypeExtension<NarrowedFields>());

        (await host.PostAsync("{ narrowed }", PostureHost.AdminSupportKey))
            .Should()
            .Contain("narrowed-value")
            .And.NotContain("\"errors\"");
    }

    [Test]
    public async Task OneAttributesRoleList_IsAnyOf()
    {
        using var host = await StartAsync(g => g.AddTypeExtension<EitherRoleFields>());

        (await host.PostAsync("{ eitherRole }", PostureHost.SupportKey))
            .Should()
            .Contain("either-value")
            .And.NotContain("\"errors\"");
        (await host.PostAsync("{ eitherRole }", PostureHost.ReaderKey))
            .Should()
            .Contain("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task APolicyAndARoleOnOneResolver_RequireBoth()
    {
        using var host = await StartAsync(g => g.AddTypeExtension<PolicyAndRoleFields>());

        (await host.PostAsync("{ policyAndRole }", PostureHost.AdminKey))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr);
        (await host.PostAsync("{ policyAndRole }", PostureHost.AdminSupportKey))
            .Should()
            .Contain("policy-and-role")
            .And.NotContain("\"errors\"");
    }

    // ── Two attributes on a query model ──────────────────────────────────

    [Test]
    public async Task TwoRoleAttributesOnAModel_RequireBothRoles()
    {
        using var host = await PostureHost.StartAsync(
            g => g.AddDbContext<TwoRoleContext>(),
            s =>
                s.AddInMemoryContext<TwoRoleContext>(c =>
                    c.Notes.Add(new TwoRoleNote { Id = 1, Body = "two-role-note" })
                )
        );
        const string query = "{ discover { twoRoleNotes { nodes { body } } } }";

        (await host.PostAsync(query, PostureHost.SupportKey))
            .Should()
            .Contain("TRAX_AUTHORIZATION")
            .And.NotContain("two-role-note");
        (await host.PostAsync(query, PostureHost.AdminSupportKey))
            .Should()
            .Contain("two-role-note")
            .And.NotContain("\"errors\"");
    }

    // ── GateOperations ───────────────────────────────────────────────────

    [Test]
    public async Task TwoGateOperationsRoleCalls_RequireBothRoles()
    {
        using var host = await PostureHost.StartAsync(g =>
            g.ExposeOperationQueries()
                .GateOperations(roles: "Admin")
                .GateOperations(roles: "Support")
        );
        const string query = "{ operations { __typename } }";

        (await host.PostAsync(query, PostureHost.SupportKey))
            .Should()
            .Contain("TRAX_AUTHORIZATION");
        (await host.PostAsync(query, PostureHost.AdminSupportKey))
            .Should()
            .Contain("OperationsQueries")
            .And.NotContain("\"errors\"");
    }

    [TestCase(",")]
    [TestCase(" , ,")]
    public async Task GateOperationsWithARoleListOfNoRoles_RefusesStartup(string roles)
    {
        var start = () =>
            PostureHost.StartAsync(g => g.ExposeOperationQueries().GateOperations(roles: roles));

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("names no role");
    }

    [Test]
    public async Task AResolverRoleListOfNoRoles_RefusesStartup()
    {
        var start = () => StartAsync(g => g.AddTypeExtension<NoRoleFields>());

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("RootQuery.noRole")
            .And.Contain("names no role");
    }

    [Test]
    public async Task AResolverWithAnEmptyPolicy_RefusesStartup()
    {
        var start = () => StartAsync(g => g.AddTypeExtension<EmptyPolicyFields>());

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("RootQuery.emptyPolicy")
            .And.Contain("empty Policy");
    }

    [Test]
    public async Task AModelMembersRoleListOfNoRoles_RefusesStartup()
    {
        var start = () =>
            PostureHost.StartAsync(
                g => g.AddDbContext<NoRoleMemberContext>(),
                s => s.AddInMemoryContext<NoRoleMemberContext>()
            );

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("NoRoleMemberNote.secret")
            .And.Contain("names no role");
    }

    private static Task<IHost> StartAsync(Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure) =>
        PostureHost.StartAsync(g =>
            configure(g.ExposeOperationQueries().AllowAnonymousOperations())
        );

    [ExtendObjectType("RootQuery")]
    [TraxAuthorize(Roles = "Admin")]
    public sealed class NarrowedFields
    {
        [TraxAuthorize(Roles = "Support")]
        public string Narrowed() => "narrowed-value";
    }

    [ExtendObjectType("RootQuery")]
    public sealed class EitherRoleFields
    {
        [TraxAuthorize(Roles = "Admin,Support")]
        public string EitherRole() => "either-value";
    }

    [ExtendObjectType("RootQuery")]
    public sealed class NoRoleFields
    {
        [TraxAuthorize(Roles = " , ")]
        public string NoRole() => "no-role";
    }

    [ExtendObjectType("RootQuery")]
    public sealed class EmptyPolicyFields
    {
        [TraxAuthorize(Policy = " ")]
        public string EmptyPolicy() => "empty-policy";
    }

    public sealed class NoRoleMemberContext(DbContextOptions<NoRoleMemberContext> options)
        : DbContext(options)
    {
        public DbSet<NoRoleMemberNote> Notes => Set<NoRoleMemberNote>();
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public sealed class NoRoleMemberNote
    {
        public int Id { get; set; }

        [TraxAuthorize(Roles = ",")]
        public string GetSecret() => "secret";
    }

    [ExtendObjectType("RootQuery")]
    public sealed class PolicyAndRoleFields
    {
        [TraxAuthorize(Policy = PostureHost.AdminPolicy)]
        [TraxAuthorize(Roles = "Support")]
        public string PolicyAndRole() => "policy-and-role";
    }

    public sealed class TwoRoleContext(DbContextOptions<TwoRoleContext> options)
        : DbContext(options)
    {
        public DbSet<TwoRoleNote> Notes => Set<TwoRoleNote>();
    }

    [TraxQueryModel]
    [TraxAuthorize(Roles = "Admin")]
    [TraxAuthorize(Roles = "Support")]
    public sealed class TwoRoleNote
    {
        public int Id { get; set; }

        public string Body { get; set; } = "";
    }
}
