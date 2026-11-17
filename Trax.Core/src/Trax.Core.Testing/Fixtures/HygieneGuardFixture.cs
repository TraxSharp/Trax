using NUnit.Framework;
using Trax.Core.Testing.Guards;

// The [Test] method names are the documentation; XML doc comments on them would be pure redundancy.
#pragma warning disable CS1591

namespace Trax.Core.Testing.Fixtures;

/// <summary>
/// Pre-written test-hygiene guards. A consumer subclasses this, overrides <see cref="Options"/> if the
/// defaults do not fit, and runs <c>dotnet test</c>; the inherited <c>[Test]</c> methods are discovered
/// in the consumer's assembly. No test bodies to write.
/// </summary>
/// <remarks>
/// Example:
/// <code>
/// [TestFixture]
/// public sealed class MyHygieneGuards : HygieneGuardFixture
/// {
///     protected override ArchitectureGuardOptions Options => new() { TestScanRoots = ["tests"] };
/// }
/// </code>
/// </remarks>
[TestFixture]
public abstract class HygieneGuardFixture
{
    /// <summary>Guard configuration. Defaults to scanning <c>tests/</c>; override to change roots or allowlists.</summary>
    protected virtual ArchitectureGuardOptions Options => new();

    [Test]
    public void Tests_do_not_use_the_Ignore_attribute()
    {
        var result = HygieneGuards.NoIgnoreAttribute(Options);
        GuardAssert.CheckedAndClean(result);
    }

    [Test]
    public void Tests_do_not_use_legacy_asserts()
    {
        var result = HygieneGuards.NoLegacyAsserts(Options);
        GuardAssert.CheckedAndClean(result);
    }

    [Test]
    public void Tests_do_not_use_fixed_delays()
    {
        var result = HygieneGuards.NoFixedDelays(Options);
        GuardAssert.CheckedAndClean(result);
    }

    [Test]
    public void Tests_do_not_check_a_substitute_before_the_started_work_ran()
    {
        var result = HygieneGuards.NoUnsynchronisedAssertAfterStart(Options);
        GuardAssert.CheckedAndClean(result);
    }
}
