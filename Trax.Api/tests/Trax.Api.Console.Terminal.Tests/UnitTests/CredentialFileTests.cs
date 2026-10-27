using System.Runtime.InteropServices;
using AwesomeAssertions;
using Trax.Api.Console.Terminal.Connection;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// The flags a credential file is opened with on Linux, by architecture, against the kernel's <c>uapi</c> headers:
/// <c>asm-generic/fcntl.h</c>, and the overrides in <c>arch/arm</c>, <c>arch/arm64</c> and <c>arch/powerpc</c>.
/// </summary>
[TestFixture]
public class CredentialFileTests
{
    private const int NonBlock = 0x800;
    private const int NoCtty = 0x100;
    private const int CloseOnExec = 0x80000;
    private const int GenericNoFollow = 0x20000;
    private const int ArmAndPowerPcNoFollow = 0x8000;

    [TestCase(Architecture.X86)]
    [TestCase(Architecture.X64)]
    [TestCase(Architecture.S390x)]
    [TestCase(Architecture.LoongArch64)]
    [TestCase(Architecture.RiscV64)]
    public void An_architecture_on_the_generic_values_opens_with_the_generic_O_NOFOLLOW(
        Architecture architecture
    ) =>
        CredentialFile
            .LinuxOpenFlags(architecture)
            .Should()
            .Be(NonBlock | NoCtty | CloseOnExec | GenericNoFollow);

    [TestCase(Architecture.Arm)]
    [TestCase(Architecture.Armv6)]
    [TestCase(Architecture.Arm64)]
    [TestCase(Architecture.Ppc64le)]
    public void ARM_and_PowerPC_open_with_their_own_O_NOFOLLOW(Architecture architecture)
    {
        var flags = CredentialFile.LinuxOpenFlags(architecture);

        flags.Should().Be(NonBlock | NoCtty | CloseOnExec | ArmAndPowerPcNoFollow);
        (flags!.Value & GenericNoFollow)
            .Should()
            .Be(0, "0x20000 is O_LARGEFILE on ARM and O_DIRECT on PowerPC, not O_NOFOLLOW");
    }

    [Test]
    public void An_architecture_not_listed_has_no_flags_so_the_file_is_not_read() =>
        CredentialFile.LinuxOpenFlags(Architecture.Wasm).Should().BeNull();

    [Test]
    public void Every_architecture_is_listed_or_refused_on_purpose()
    {
        var unlisted = Enum.GetValues<Architecture>()
            .Where(architecture => CredentialFile.LinuxOpenFlags(architecture) is null)
            .ToList();

        unlisted
            .Should()
            .BeEquivalentTo(
                [Architecture.Wasm],
                "a new architecture in .NET is refused until its values are checked against its headers"
            );
    }
}
