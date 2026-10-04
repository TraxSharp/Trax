using AwesomeAssertions;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Plain <c>Train.Run</c> stays virtual and unsealed. The published Trax.Effect overrides it in
/// <c>ServiceTrain</c> and accepts a newer Trax.Core, so making it non-virtual would stop every
/// published <c>ServiceTrain</c> loading.
///
/// <para>Enforces Trax.Core/docs/adr/0003-plain-train-keeps-an-overridable-run.md.</para>
/// </summary>
[Property("adr", "Trax.Core/docs/adr/0003-plain-train-keeps-an-overridable-run.md")]
public class PlainTrainRunStaysVirtualTests
{
    private const string Adr =
        "see Trax.Core/docs/adr/0003-plain-train-keeps-an-overridable-run.md";

    [Test]
    public void Run_StaysVirtualAndUnsealed()
    {
        var train = typeof(Trax.Core.Train.Train<,>);
        var run = train.GetMethods().Single(m => m.Name == "Run" && m.DeclaringType == train);

        run.IsVirtual.Should()
            .BeTrue(
                $"the published Trax.Effect overrides Train.Run in ServiceTrain, and a non-virtual "
                    + $"Run makes every published ServiceTrain fail to load ({Adr})"
            );
        run.IsFinal.Should().BeFalse($"a sealed Run breaks the same override ({Adr})");
    }
}
