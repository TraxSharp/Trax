namespace Trax.Mediator.Tests.Meta.Tests;

/// <summary>
/// Cross-repo Trax packages are published one family per repo: all <c>Trax.Effect*</c> ship together from
/// Trax.Effect, all <c>Trax.Api*</c> from Trax.Api, and so on. So within a consumer's
/// <c>Directory.Packages.props</c> every pin in a family must resolve to the SAME version. A partial bump
/// (moving <c>Trax.Effect</c> to a new release but leaving <c>Trax.Effect.StateMachine.Persistence</c> behind)
/// is a latent break: the two halves of one release no longer agree, which surfaces as an NU1605 downgrade or a
/// silent behaviour mismatch. This guard fails fast on that drift, offline, with no network or NuGet lookup.
///
/// <para>Enforces <c>Trax.Docs/adr/0002-cross-repo-dependencies-are-exact-pinned.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0002-cross-repo-dependencies-are-exact-pinned.md")]
[TestFixture]
public class TraxPinLockstepTests
{
    [Test]
    public void EveryTraxPin_InAFamily_SharesOneVersion()
    {
        var props = RepoRoot.Combine("Directory.Packages.props");
        File.Exists(props)
            .Should()
            .BeTrue(
                $"every Trax repo pins cross-repo packages in Directory.Packages.props; none at '{props}'."
            );

        var doc = XDocument.Load(props);

        var pins = doc.Descendants("PackageVersion")
            .Select(e =>
                (
                    Id: e.Attribute("Include")?.Value,
                    Version: ExtractVersion(
                        e.Attribute("Version")?.Value ?? e.Element("Version")?.Value
                    )
                )
            )
            .Where(p =>
                !string.IsNullOrEmpty(p.Id)
                && p.Id!.StartsWith("Trax.", StringComparison.Ordinal)
                && p.Version is not null
            )
            .ToList();

        var drift = pins.GroupBy(p => Family(p.Id!), StringComparer.Ordinal)
            .Where(g => g.Select(p => p.Version).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g =>
                $"{g.Key}: "
                + string.Join(
                    ", ",
                    g.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => $"{p.Id}={p.Version}")
                )
            )
            .ToList();

        drift
            .Should()
            .BeEmpty(
                "every Trax package in a family is published together and must pin the same version; a split "
                    + "pin is a partial bump that breaks the release (e.g. the IEffect -> ISnapshotEffect rename "
                    + "lands in one sub-package but not another). Drifted families:\n  "
                    + string.Join("\n  ", drift)
            );
    }

    // The publishing repo, i.e. the first two dotted segments: Trax.Effect, Trax.Api, Trax.Core, Trax.Mediator,
    // Trax.Scheduler, Trax.Dashboard. All of a repo's sub-packages share this family and release in lockstep.
    private static string Family(string packageId)
    {
        var parts = packageId.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : packageId;
    }

    // Pins are plain literal versions so Dependabot can read them; local dev overrides them through a
    // separate `PackageVersion Update=` group that trax-local.props switches on, and those carry no
    // Include so they never reach here. The ValueOrDefault form is still accepted for a repo that has
    // not been converted yet.
    private static string? ExtractVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var m = Regex.Match(raw, @"ValueOrDefault\([^,]*,\s*'([^']+)'\s*\)");
        if (m.Success)
            return m.Groups[1].Value;
        return Regex.IsMatch(raw, @"^\d+\.\d+\.\d+") ? raw : null;
    }
}
