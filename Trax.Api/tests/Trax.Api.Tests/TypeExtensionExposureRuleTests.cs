using FluentAssertions;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.Tests;

/// <summary>
/// The decision matrix for a field a type extension adds to a type Trax owns. Every combination
/// of (parent posture x [Authorize] x [AllowAnonymous] x endpoint posture) is pinned here, because
/// the rule is what decides whether a host boots, and the wiring tests in
/// <see cref="TypeExtensionExposureTests"/> only sample it.
/// </summary>
[Property("adr", "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md")]
[TestFixture]
public class TypeExtensionExposureRuleTests
{
    /// <summary>
    /// All 24 combinations, with the expected answer spelled out rather than computed, so a change
    /// to the rule has to change this table too.
    /// </summary>
    private static IEnumerable<TestCaseData> Matrix()
    {
        // Endpoint open. Only a field whose parent gives it nothing has to declare.
        yield return Case(
            TypeExtensionParentPosture.Anonymous,
            false,
            false,
            false,
            ExposureViolation.MissingMarker
        );
        yield return Case(
            TypeExtensionParentPosture.Anonymous,
            true,
            false,
            false,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Anonymous,
            false,
            true,
            false,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Anonymous,
            true,
            true,
            false,
            ExposureViolation.Conflict
        );

        yield return Case(
            TypeExtensionParentPosture.Gated,
            false,
            false,
            false,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Gated,
            true,
            false,
            false,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Gated,
            false,
            true,
            false,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Gated,
            true,
            true,
            false,
            ExposureViolation.Conflict
        );

        yield return Case(
            TypeExtensionParentPosture.NotExposed,
            false,
            false,
            false,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.NotExposed,
            true,
            false,
            false,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.NotExposed,
            false,
            true,
            false,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.NotExposed,
            true,
            true,
            false,
            ExposureViolation.Conflict
        );

        // Endpoint gated. Nothing has to declare, because the endpoint already rejects the
        // anonymous caller, but declaring two contradictory things is still incoherent.
        yield return Case(
            TypeExtensionParentPosture.Anonymous,
            false,
            false,
            true,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Anonymous,
            true,
            false,
            true,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Anonymous,
            false,
            true,
            true,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Anonymous,
            true,
            true,
            true,
            ExposureViolation.Conflict
        );

        yield return Case(
            TypeExtensionParentPosture.Gated,
            false,
            false,
            true,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Gated,
            true,
            false,
            true,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Gated,
            false,
            true,
            true,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.Gated,
            true,
            true,
            true,
            ExposureViolation.Conflict
        );

        yield return Case(
            TypeExtensionParentPosture.NotExposed,
            false,
            false,
            true,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.NotExposed,
            true,
            false,
            true,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.NotExposed,
            false,
            true,
            true,
            ExposureViolation.None
        );
        yield return Case(
            TypeExtensionParentPosture.NotExposed,
            true,
            true,
            true,
            ExposureViolation.Conflict
        );

        static TestCaseData Case(
            TypeExtensionParentPosture parent,
            bool authorize,
            bool anonymous,
            bool gated,
            ExposureViolation expected
        ) =>
            new TestCaseData(parent, authorize, anonymous, gated, expected).SetName(
                $"{parent}_"
                    + $"{(authorize ? "Authorize" : "NoAuthorize")}_"
                    + $"{(anonymous ? "AllowAnonymous" : "NoAllowAnonymous")}_"
                    + $"{(gated ? "EndpointGated" : "EndpointOpen")}"
            );
    }

    /// <summary>
    /// The enums are internal, and a public test method cannot name them in its signature, so the
    /// case data carries them as <see cref="object"/> and this casts them back. The alternative,
    /// widening two internal enums to public, would put them in the committed API baseline for the
    /// sake of a test.
    /// </summary>
    [TestCaseSource(nameof(Matrix))]
    public void Evaluate_MatchesTheMatrix(
        object parent,
        bool hasAuthorize,
        bool hasAllowAnonymous,
        bool endpointGated,
        object expected
    )
    {
        TypeExtensionExposureRule
            .Evaluate(
                (TypeExtensionParentPosture)parent,
                hasAuthorize,
                hasAllowAnonymous,
                endpointGated
            )
            .Should()
            .Be(
                (ExposureViolation)expected,
                "the decision matrix is the decision recorded in "
                    + "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md; changing "
                    + "an answer here changes which hosts boot"
            );
    }

    /// <summary>
    /// The one place this rule deliberately disagrees with <see cref="ExposureAuthorizationRule"/>.
    /// An entity's <c>[TraxAllowAnonymous]</c> under a gated endpoint is reported, because the
    /// entity gate is all or nothing and the endpoint already answered. A field's
    /// <c>[AllowAnonymous]</c> is not, because on a role-gated parent it still means something:
    /// any authenticated caller, not just the role.
    /// </summary>
    [Test]
    public void AllowAnonymousUnderGate_IsReportedForAnEntity_ButNotForAField()
    {
        ExposureAuthorizationRule
            .Evaluate(hasAuthorize: false, hasAllowAnonymous: true, endpointGated: true)
            .Should()
            .Be(ExposureViolation.AnonymousUnderGate);

        TypeExtensionExposureRule
            .Evaluate(
                TypeExtensionParentPosture.Anonymous,
                hasAuthorize: false,
                hasAllowAnonymous: true,
                endpointGated: true
            )
            .Should()
            .Be(ExposureViolation.None);
    }

    // ── Messages ────────────────────────────────────────────────────────

    [Test]
    public void MissingMarkerMessage_NamesTheFieldTheResolverAndTheWayOut()
    {
        var message = TypeExtensionExposureRule.BuildMessage(
            "Issue.content",
            "Nwyc.IssueContentExtension.GetContent",
            "'Issue', which is [TraxAllowAnonymous]",
            ExposureViolation.MissingMarker
        );

        message.Should().Contain("Issue.content");
        message.Should().Contain("Nwyc.IssueContentExtension.GetContent");
        message.Should().Contain("[TraxAuthorize]");
        message.Should().Contain("[TraxAllowAnonymous]");
        message.Should().Contain("RequireAuthorization");
    }

    /// <summary>
    /// The message names Trax's attributes and says they apply to a method, because the first
    /// thing a consumer needs to know is that the Trax vocabulary reaches a resolver now.
    /// </summary>
    [Test]
    public void MissingMarkerMessage_SaysTheTraxAttributesApplyToAMethod()
    {
        TypeExtensionExposureRule
            .BuildMessage(
                "X.y",
                "A.B",
                "the schema root type 'RootQuery'",
                ExposureViolation.MissingMarker
            )
            .Should()
            .Contain("Both apply to a method");
    }

    [Test]
    public void ConflictMessage_NamesBothAttributes()
    {
        var message = TypeExtensionExposureRule.BuildMessage(
            "Issue.content",
            "A.B",
            "'Issue', which is [TraxAllowAnonymous]",
            ExposureViolation.Conflict
        );

        message.Should().Contain("[TraxAuthorize]");
        message.Should().Contain("[TraxAllowAnonymous]");
        message.Should().Contain("Pick one");
    }

    /// <summary>
    /// <see cref="ExposureViolation.AnonymousUnderGate"/> is unreachable for a field, so asking for
    /// its message is a programming error rather than something to render.
    /// </summary>
    [Test]
    public void BuildMessage_ForAnUnreachableViolation_Throws()
    {
        var act = () =>
            TypeExtensionExposureRule.BuildMessage(
                "X.y",
                "A.B",
                "parent",
                ExposureViolation.AnonymousUnderGate
            );

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void BuildMessage_ForNone_Throws()
    {
        var act = () =>
            TypeExtensionExposureRule.BuildMessage("X.y", "A.B", "parent", ExposureViolation.None);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
