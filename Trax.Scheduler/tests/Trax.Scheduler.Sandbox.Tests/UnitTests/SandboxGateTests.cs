using Trax.Scheduler.Sandbox.Launcher.Core;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>The concurrency cap: past it, refused at once; a released slot is reusable; a slot released twice frees one.</summary>
[TestFixture]
public class SandboxGateTests
{
    [Test]
    public void TryEnter_UpToTheCap_ThenNull()
    {
        var gate = new SandboxGate(2);
        using var a = gate.TryEnter();
        using var b = gate.TryEnter();

        a.Should().NotBeNull();
        b.Should().NotBeNull();
        gate.TryEnter().Should().BeNull();
        gate.Running.Should().Be(2);
    }

    [Test]
    public void Dispose_FreesTheSlot_Once()
    {
        var gate = new SandboxGate(1);
        var slot = gate.TryEnter();
        slot?.Dispose();
        slot?.Dispose();

        gate.Running.Should().Be(0);
        using var again = gate.TryEnter();
        again.Should().NotBeNull();
        gate.TryEnter().Should().BeNull("a double dispose must not open a second slot");
    }

    [Test]
    public async Task TryEnter_ManyAtOnce_NeverMoreThanTheCap()
    {
        var gate = new SandboxGate(3);
        var granted = 0;
        await Task.WhenAll(
            Enumerable
                .Range(0, 64)
                .Select(_ =>
                    Task.Run(() =>
                    {
                        if (gate.TryEnter() is not null)
                            Interlocked.Increment(ref granted);
                    })
                )
        );

        granted.Should().Be(3);
    }
}
