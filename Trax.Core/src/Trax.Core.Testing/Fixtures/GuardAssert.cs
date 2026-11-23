using NUnit.Framework;
using Trax.Core.Testing.Guards;

// The [Test] method names are the documentation; XML doc comments on them would be pure redundancy.
#pragma warning disable CS1591

namespace Trax.Core.Testing.Fixtures;

/// <summary>
/// The assertion both fixtures make: the guard looked at something, and found nothing wrong.
/// </summary>
internal static class GuardAssert
{
    /// <summary>
    /// A guard that inspected nothing proves nothing: a repo whose tests live somewhere other
    /// than the configured roots would otherwise pass every check. Fails first on that, then on
    /// any offender.
    /// </summary>
    public static void CheckedAndClean(GuardResult result)
    {
        Assert.That(
            result.Inspected,
            Is.GreaterThan(0),
            "The guard inspected no files, so it checked nothing. Point the options' scan roots at "
                + "the directories that hold this repo's sources and tests."
        );
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }
}
