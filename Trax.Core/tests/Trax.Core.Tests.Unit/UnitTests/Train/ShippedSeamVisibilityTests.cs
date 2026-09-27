using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using Trax.Core.Exceptions;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// <c>Train.NewMonad()</c> and <c>ChainRecordedException</c> shipped public and stay that way,
/// hidden from completion. Narrowing <c>NewMonad()</c> makes the published Trax.Effect's
/// <c>ServiceTrain</c>, which overrides it as <c>protected override</c>, fail to load.
///
/// <para>Enforces Trax.Core/docs/adr/0002-a-shipped-seam-stays-public-and-hidden.md.</para>
/// </summary>
[Property("adr", "Trax.Core/docs/adr/0002-a-shipped-seam-stays-public-and-hidden.md")]
public class ShippedSeamVisibilityTests
{
    private const string Adr =
        "see Trax.Core/docs/adr/0002-a-shipped-seam-stays-public-and-hidden.md";

    [Test]
    public void NewMonad_StaysProtectedVirtualAndHidden()
    {
        var newMonad = typeof(Trax.Core.Train.Train<,>).GetMethod(
            "NewMonad",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        newMonad
            .IsFamily.Should()
            .BeTrue(
                $"the published Trax.Effect overrides NewMonad as protected, and narrowing it "
                    + $"makes every ServiceTrain fail to load ({Adr})"
            );
        newMonad.IsVirtual.Should().BeTrue($"ServiceTrain overrides NewMonad ({Adr})");
        Hidden(newMonad).Should().BeTrue($"NewMonad is not a consumer extension point ({Adr})");
    }

    [Test]
    public void ChainRecordedException_StaysPublicAndHidden()
    {
        typeof(ChainRecordedException)
            .IsPublic.Should()
            .BeTrue($"it shipped public and is kept with NewMonad ({Adr})");
        Hidden(typeof(ChainRecordedException))
            .Should()
            .BeTrue($"it is a sentinel nobody should catch ({Adr})");
    }

    private static bool Hidden(MemberInfo member) =>
        member.GetCustomAttribute<EditorBrowsableAttribute>()?.State == EditorBrowsableState.Never;
}
