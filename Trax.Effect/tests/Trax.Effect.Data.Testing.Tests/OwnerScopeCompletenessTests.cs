using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>
/// The owner-scope census recognises per-user data from the model, counts only a filter that reads
/// the principal, and refuses every GraphQL posture on a per-user entity but a bare
/// <c>[TraxAuthorize]</c>.
///
/// <para>Enforces <c>docs/adr/0008-per-user-data-is-filtered-by-its-owner.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0008-per-user-data-is-filtered-by-its-owner.md")]
public class OwnerScopeCompletenessTests
{
    private static readonly Dictionary<Type, string> AnswerIsNavigationScoped = new()
    {
        [typeof(Answer)] = "an answer belongs to the poll that holds the account id",
    };

    private static OwnerScopeCensusOptions Options(
        IReadOnlyDictionary<Type, string>? navigationScoped = null,
        IReadOnlyDictionary<Type, string>? exemptions = null,
        IReadOnlySet<string>? ownerIdProperties = null,
        IReadOnlyDictionary<Type, string>? gated = null
    ) =>
        new()
        {
            OwnerType = typeof(Account),
            PrincipalAccessorType = typeof(IOwnerPrincipal),
            NavigationScoped = navigationScoped ?? new Dictionary<Type, string>(),
            Exemptions = exemptions ?? new Dictionary<Type, string>(),
            OwnerIdProperties = ownerIdProperties ?? new HashSet<string>(),
            Gated = gated ?? new Dictionary<Type, string>(),
        };

    private static Dictionary<Type, string> Gate(Type type, string reason = "a paid feature") =>
        new() { [type] = reason };

    private static IModel ModelOf<TContext>()
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=localhost;Database=offline_model_only")
            .Options;

        using var context = (TContext)
            Activator.CreateInstance(typeof(TContext), options, new NoPrincipal())!;
        return context.Model;
    }

    [Test]
    public void Passes_when_every_per_user_entity_is_filtered_through_the_principal()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<ScopedContext>(),
            Options(navigationScoped: AnswerIsNavigationScoped)
        );

        result.Passed.Should().BeTrue(result.FailureMessage);
        result
            .Inspected.Should()
            .Be(
                6,
                "Account, Note, PinnedNote, Poll, Answer and ExposedNote hold per-user data; "
                    + "Article and PublishedArticle carry a filter that does not consult the "
                    + "principal, so they are not per-user and a role gate on them is fine"
            );
    }

    [Test]
    public void Flags_an_owner_foreign_key_with_no_row_filter()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<UnfilteredContext>(),
            Options()
        );

        result
            .Offenders.Should()
            .ContainSingle(
                "0008-per-user-data-is-filtered-by-its-owner.md: an "
                    + "owner key with no row filter serves every owner's rows to any caller"
            )
            .Which.Should()
            .Contain(nameof(Note));
        result.FailureMessage.Should().Contain("HasQueryFilter");
    }

    [Test]
    public void Flags_an_owner_foreign_key_whose_only_filter_ignores_the_principal()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<VisibilityOnlyContext>(),
            Options()
        );

        result
            .Offenders.Should()
            .ContainSingle(
                "a filter that never reads the principal scopes nothing to the owner, "
                    + "whatever else it hides"
            )
            .Which.Should()
            .Contain(nameof(Note));
    }

    [Test]
    public void A_derived_type_is_covered_by_the_filter_on_its_root()
    {
        // EF declares a query filter on the hierarchy root only; PinnedNote has none of its own.
        var model = ModelOf<ScopedContext>();
        model.FindEntityType(typeof(PinnedNote))!.GetDeclaredQueryFilters().Should().BeEmpty();

        var result = DataLayerGuards.OwnerScopeCompleteness(
            model,
            Options(navigationScoped: AnswerIsNavigationScoped)
        );

        result.Offenders.Should().NotContain(o => o.Contains(nameof(PinnedNote)));
    }

    [Test]
    public void Flags_a_scalar_owner_id_named_by_the_options()
    {
        var withoutName = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<ScalarOwnerContext>(),
            Options()
        );
        withoutName.Passed.Should().BeTrue("OwnerId is only an owner key when the options say so");

        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<ScalarOwnerContext>(),
            Options(ownerIdProperties: new HashSet<string> { nameof(AuditRow.OwnerId) })
        );

        result.Offenders.Should().ContainSingle().Which.Should().Contain(nameof(AuditRow));
    }

    [Test]
    public void Flags_a_navigation_scoped_entity_that_is_not_declared()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(ModelOf<ScopedContext>(), Options());

        result
            .Offenders.Should()
            .ContainSingle(
                "an entity scoped only through a navigation drops out of the census silently "
                    + "the day its filter goes, so it has to be named"
            )
            .Which.Should()
            .Contain(nameof(Answer));
    }

    [Test]
    public void Flags_a_declared_navigation_scoped_entity_that_lost_its_filter()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<UnfilteredContext>(),
            Options(
                navigationScoped: AnswerIsNavigationScoped,
                exemptions: new Dictionary<Type, string>
                {
                    [typeof(Note)] = "this test is about Answer, which the context does not map",
                }
            )
        );

        result.Offenders.Should().ContainSingle().Which.Should().Contain(nameof(Answer));
    }

    [Test]
    public void Flags_each_graphql_posture_that_is_not_a_bare_TraxAuthorize()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(ModelOf<PostureContext>(), Options());

        result.Offenders.Should().HaveCount(4);
        result
            .Offenders.Should()
            .ContainSingle(o => o.Contains(nameof(AnonymousNote)))
            .Which.Should()
            .Contain("[TraxAllowAnonymous]");
        result
            .Offenders.Should()
            .ContainSingle(o => o.Contains(nameof(UndeclaredNote)))
            .Which.Should()
            .Contain("without [TraxAuthorize]");
        result
            .Offenders.Should()
            .ContainSingle(o => o.Contains(nameof(RoleGatedNote)))
            .Which.Should()
            .Contain("subscriber");
        result
            .Offenders.Should()
            .ContainSingle(o => o.Contains(nameof(PolicyGatedNote)))
            .Which.Should()
            .Contain("premium", "a gate declared on an implemented interface still gates");
    }

    [Test]
    public void An_exemption_with_a_reason_skips_the_entity()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<UnfilteredContext>(),
            Options(
                exemptions: new Dictionary<Type, string>
                {
                    [typeof(Note)] = "admin-only table, never read on a caller's behalf",
                }
            )
        );

        result.Passed.Should().BeTrue(result.FailureMessage);
    }

    [Test]
    public void An_exemption_without_a_reason_is_refused()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<UnfilteredContext>(),
            Options(exemptions: new Dictionary<Type, string> { [typeof(Note)] = "  " })
        );

        result.Offenders.Should().ContainSingle().Which.Should().Contain("reason");
    }

    [Test]
    public void An_exemption_for_an_entity_the_census_would_not_flag_is_refused()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<ScopedContext>(),
            Options(
                navigationScoped: AnswerIsNavigationScoped,
                exemptions: new Dictionary<Type, string>
                {
                    [typeof(Article)] = "was per-user once, and the exemption outlived it",
                }
            )
        );

        result
            .Offenders.Should()
            .ContainSingle("a stale exemption would silently cover whatever is added next")
            .Which.Should()
            .Contain(nameof(Article));
    }

    [Test]
    public void Refuses_options_without_an_owner_or_an_accessor()
    {
        var act = () =>
            DataLayerGuards.OwnerScopeCompleteness(
                ModelOf<ScopedContext>(),
                Options() with
                {
                    PrincipalAccessorType = null!,
                }
            );

        act.Should().Throw<ArgumentNullException>();
    }

    #region Gated

    [Test]
    public void A_gated_entity_with_its_owner_filter_passes()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<GatedContext>(),
            Options(gated: Gate(typeof(PremiumNote)))
        );

        result.Passed.Should().BeTrue(result.FailureMessage);
        result.Inspected.Should().Be(2, "Account and PremiumNote");
    }

    [Test]
    public void A_role_gate_without_a_gated_entry_is_still_refused()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(ModelOf<GatedContext>(), Options());

        result.Offenders.Should().ContainSingle().Which.Should().Contain("premium");
    }

    [Test]
    public void A_gated_entity_still_needs_its_owner_filter()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<GatedUnfilteredContext>(),
            Options(gated: Gate(typeof(PremiumNote)))
        );

        result
            .Offenders.Should()
            .ContainSingle("the gate is added to the filter, never used in place of it")
            .Which.Should()
            .Contain("HasQueryFilter");
    }

    [Test]
    public void Gating_never_admits_an_anonymous_or_undeclared_posture()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<PostureContext>(),
            Options(
                gated: new Dictionary<Type, string>
                {
                    [typeof(AnonymousNote)] = "tries to excuse an anonymous posture",
                    [typeof(UndeclaredNote)] = "tries to excuse a missing posture",
                    [typeof(RoleGatedNote)] = "subscribers only",
                    [typeof(PolicyGatedNote)] = "premium only",
                }
            )
        );

        result
            .Offenders.Should()
            .Contain(o => o.Contains(nameof(AnonymousNote)) && o.Contains("[TraxAllowAnonymous]"))
            .And.Contain(o =>
                o.Contains(nameof(UndeclaredNote)) && o.Contains("without [TraxAuthorize]")
            )
            .And.Contain(o => o.Contains(nameof(AnonymousNote)) && o.Contains("gated"))
            .And.Contain(o => o.Contains(nameof(UndeclaredNote)) && o.Contains("gated"))
            .And.NotContain(o => o.Contains(nameof(RoleGatedNote)))
            .And.NotContain(o => o.Contains(nameof(PolicyGatedNote)));
    }

    [Test]
    public void A_gated_entry_needs_a_reason()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<GatedContext>(),
            Options(gated: Gate(typeof(PremiumNote), " "))
        );

        result.Offenders.Should().ContainSingle().Which.Should().Contain("reason");
    }

    [Test]
    public void A_gated_entry_for_a_bare_TraxAuthorize_is_stale()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<ScopedContext>(),
            Options(navigationScoped: AnswerIsNavigationScoped, gated: Gate(typeof(ExposedNote)))
        );

        result.Offenders.Should().ContainSingle().Which.Should().Contain(nameof(ExposedNote));
    }

    [Test]
    public void A_gated_entry_for_an_entity_that_is_not_per_user_is_stale()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<ScopedContext>(),
            Options(
                navigationScoped: AnswerIsNavigationScoped,
                gated: Gate(typeof(PublishedArticle))
            )
        );

        result.Offenders.Should().ContainSingle().Which.Should().Contain(nameof(PublishedArticle));
    }

    [Test]
    public void A_gated_entry_for_an_entity_that_is_not_exposed_is_stale()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<ScopedContext>(),
            Options(navigationScoped: AnswerIsNavigationScoped, gated: Gate(typeof(Poll)))
        );

        result.Offenders.Should().ContainSingle().Which.Should().Contain(nameof(Poll));
    }

    [Test]
    public void An_entity_cannot_be_both_gated_and_exempted()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<GatedUnfilteredContext>(),
            Options(
                gated: Gate(typeof(PremiumNote)),
                exemptions: new Dictionary<Type, string>
                {
                    [typeof(PremiumNote)] = "admin tooling reads it unfiltered",
                }
            )
        );

        result
            .Offenders.Should()
            .ContainSingle(
                "an exemption skips the filter check, so combined with a gate it would let the gate "
                    + "stand in for the filter"
            )
            .Which.Should()
            .Contain("both");
    }

    #endregion

    #region Shared storage

    [Test]
    public void An_entity_reading_a_per_user_table_through_another_mapping_is_per_user()
    {
        var result = DataLayerGuards.OwnerScopeCompleteness(
            ModelOf<AliasedTableContext>(),
            Options(gated: Gate(typeof(NoteView), "admin reporting"))
        );

        result
            .Offenders.Should()
            .ContainSingle(
                "a second mapping over the same rows, with no filter, reads every owner's rows "
                    + "whatever gate sits on it"
            )
            .Which.Should()
            .Contain(nameof(NoteView))
            .And.Contain("notes");
    }

    #endregion
}
