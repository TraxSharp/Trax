using FluentAssertions;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.Tests;

/// <summary>
/// The exposure authorization rule is the single source of truth shared by the
/// query-model build validation and the train startup validation. These cases pin
/// the full decision matrix (marker presence × endpoint posture) so any drift in
/// either caller is caught here rather than in a downstream integration test.
/// </summary>
[TestFixture]
public class ExposureAuthorizationRuleTests
{
    // ── Endpoint open (no RequireAuthorization) ──────────────────────────

    [Test]
    public void Open_NoMarker_IsMissingMarker()
    {
        ExposureAuthorizationRule
            .Evaluate(hasAuthorize: false, hasAllowAnonymous: false, endpointGated: false)
            .Should()
            .Be(ExposureViolation.MissingMarker);
    }

    [Test]
    public void Open_AuthorizeOnly_IsAllowed()
    {
        ExposureAuthorizationRule
            .Evaluate(hasAuthorize: true, hasAllowAnonymous: false, endpointGated: false)
            .Should()
            .Be(ExposureViolation.None);
    }

    [Test]
    public void Open_AllowAnonymousOnly_IsAllowed()
    {
        ExposureAuthorizationRule
            .Evaluate(hasAuthorize: false, hasAllowAnonymous: true, endpointGated: false)
            .Should()
            .Be(ExposureViolation.None);
    }

    [Test]
    public void Open_BothMarkers_IsConflict()
    {
        ExposureAuthorizationRule
            .Evaluate(hasAuthorize: true, hasAllowAnonymous: true, endpointGated: false)
            .Should()
            .Be(ExposureViolation.Conflict);
    }

    // ── Endpoint gated (RequireAuthorization) ────────────────────────────

    [Test]
    public void Gated_NoMarker_IsAllowed()
    {
        // The endpoint already gates every request, so a missing marker is not an
        // implicitly-public surface.
        ExposureAuthorizationRule
            .Evaluate(hasAuthorize: false, hasAllowAnonymous: false, endpointGated: true)
            .Should()
            .Be(ExposureViolation.None);
    }

    [Test]
    public void Gated_AuthorizeOnly_IsAllowed()
    {
        // Endpoint gate + per-surface [TraxAuthorize] compose (defense in depth at
        // different granularities), so this is not a conflict.
        ExposureAuthorizationRule
            .Evaluate(hasAuthorize: true, hasAllowAnonymous: false, endpointGated: true)
            .Should()
            .Be(ExposureViolation.None);
    }

    [Test]
    public void Gated_AllowAnonymousOnly_IsAnonymousUnderGate()
    {
        // The endpoint rejects unauthenticated callers before the surface is reached,
        // so [TraxAllowAnonymous] can never take effect.
        ExposureAuthorizationRule
            .Evaluate(hasAuthorize: false, hasAllowAnonymous: true, endpointGated: true)
            .Should()
            .Be(ExposureViolation.AnonymousUnderGate);
    }

    [Test]
    public void Gated_BothMarkers_IsConflict()
    {
        // Both-markers contradiction takes precedence over the gate interaction.
        ExposureAuthorizationRule
            .Evaluate(hasAuthorize: true, hasAllowAnonymous: true, endpointGated: true)
            .Should()
            .Be(ExposureViolation.Conflict);
    }

    // ── Messages name the surface and explain the fix ────────────────────

    [Test]
    public void BuildMessage_MissingMarker_NamesSurfaceAndBothAttributes()
    {
        var msg = ExposureAuthorizationRule.BuildMessage(
            "GraphQL-exposed train",
            "My.Trains.IThingTrain",
            ExposureViolation.MissingMarker
        );

        msg.Should().Contain("My.Trains.IThingTrain");
        msg.Should().Contain("[TraxAuthorize]");
        msg.Should().Contain("[TraxAllowAnonymous]");
    }

    [Test]
    public void BuildMessage_Conflict_NamesSurface()
    {
        var msg = ExposureAuthorizationRule.BuildMessage(
            "[TraxQueryModel] entity",
            "My.Models.Thing",
            ExposureViolation.Conflict
        );

        msg.Should().Contain("My.Models.Thing");
        msg.Should().Contain("mutually exclusive");
    }

    [Test]
    public void BuildMessage_AnonymousUnderGate_MentionsRequireAuthorization()
    {
        var msg = ExposureAuthorizationRule.BuildMessage(
            "GraphQL-exposed train",
            "My.Trains.IThingTrain",
            ExposureViolation.AnonymousUnderGate
        );

        msg.Should().Contain("My.Trains.IThingTrain");
        msg.Should().Contain("RequireAuthorization");
    }

    [Test]
    public void BuildMessage_None_Throws()
    {
        // None is not a violation, so it has no message. Callers guard against it; the
        // defensive arm exists so a future enum value cannot silently produce an empty
        // string. Pinning it keeps the contract explicit.
        var act = () => ExposureAuthorizationRule.BuildMessage("x", "y", ExposureViolation.None);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
