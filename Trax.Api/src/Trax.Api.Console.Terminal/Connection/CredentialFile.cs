using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Trax.Api.Console.Terminal.Connection;

/// <summary>
/// What reading a credential file needs from the OS beyond <see cref="FileStream"/>: an open that does not wait on a
/// pipe and follows no link, on Unix, and on Windows who else the file's permissions let in.
/// </summary>
internal static class CredentialFile
{
    private const int ReadOnly = 0;

    /// <summary>
    /// <c>O_RDONLY | O_NONBLOCK | O_NOFOLLOW | O_NOCTTY | O_CLOEXEC</c> for this OS and architecture, or null on
    /// Windows, where the file is opened as usual.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// A Unix whose flag values are not known here. Opening the file with flags that mean something else (<c>O_NOFOLLOW</c>'s generic value is
    /// <c>O_LARGEFILE</c> on ARM and <c>O_DIRECT</c> on PowerPC) would drop the checks the open is for, so the file is
    /// not read at all.
    /// </exception>
    public static int? OpenFlags()
    {
        if (OperatingSystem.IsWindows())
            return null;
        if (OperatingSystem.IsLinux())
            return LinuxOpenFlags(RuntimeInformation.ProcessArchitecture)
                ?? throw Unsupported($"Linux on {RuntimeInformation.ProcessArchitecture}");
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            return ReadOnly | 0x4 | 0x100 | 0x20000 | 0x1000000;
        if (OperatingSystem.IsFreeBSD())
            return ReadOnly | 0x4 | 0x100 | 0x8000 | 0x100000;
        throw Unsupported(RuntimeInformation.OSDescription);
    }

    /// <summary>
    /// The open flags on Linux for <paramref name="architecture"/>, from the kernel's <c>uapi</c> headers, or null for
    /// an architecture not listed. <c>asm-generic/fcntl.h</c> gives <c>O_NONBLOCK</c> <c>0x800</c>, <c>O_NOCTTY</c>
    /// <c>0x100</c>, <c>O_NOFOLLOW</c> <c>0x20000</c> and <c>O_CLOEXEC</c> <c>0x80000</c>; 32- and 64-bit ARM and
    /// PowerPC override <c>O_NOFOLLOW</c> to <c>0x8000</c>, and keep the other three. MIPS, SPARC, Alpha and PA-RISC
    /// override more, and .NET does not run on them; they would be added here, value by value, if it did.
    /// </summary>
    internal static int? LinuxOpenFlags(Architecture architecture) =>
        architecture switch
        {
            Architecture.X86
            or Architecture.X64
            or Architecture.S390x
            or Architecture.LoongArch64
            or Architecture.RiscV64 => Linux(noFollow: 0x20000),
            Architecture.Arm or Architecture.Armv6 or Architecture.Arm64 or Architecture.Ppc64le =>
                Linux(noFollow: 0x8000),
            _ => null,
        };

    private static int Linux(int noFollow) => ReadOnly | 0x800 | noFollow | 0x100 | 0x80000;

    private static PlatformNotSupportedException Unsupported(string platform) =>
        new(
            $"reading a credential file is not supported on {platform}, where the flags that open it without "
                + $"following a link or waiting on a pipe are not known; set {ConsoleTarget.ApiKeyVariable} or "
                + $"{ConsoleTarget.TokenVariable} instead"
        );

    /// <summary>open(2): the descriptor, or -1 with the error in <see cref="Marshal.GetLastPInvokeError"/>.</summary>
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    public static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    // The groups that hold every account, or anyone at all: a file one of them may read is not one person's.
    private static readonly Dictionary<string, string> Everyone = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["S-1-1-0"] = "Everyone",
        ["S-1-5-7"] = "Anonymous Logon",
        ["S-1-5-4"] = "Interactive users",
        ["S-1-5-11"] = "Authenticated Users",
        ["S-1-5-32-545"] = "Users",
        ["S-1-5-32-546"] = "Guests",
    };

    // Reading or changing the file's content, as an access mask: FILE_READ_DATA, FILE_WRITE_DATA, FILE_APPEND_DATA,
    // WRITE_DAC and WRITE_OWNER, and the generic rights an entry may still carry (GENERIC_ALL, GENERIC_WRITE,
    // GENERIC_READ). A mask rather than FileSystemRights, which exists on Windows only, so the rule is testable anywhere.
    private const uint ReadOrChange =
        0x1 | 0x2 | 0x4 | 0x40000 | 0x80000 | 0x10000000 | 0x40000000 | 0x80000000;

    /// <summary>
    /// The name of a group of every account (Everyone, Users, Authenticated Users and the like) that an allow entry
    /// lets read or change the file, or null when there is none. An entry that only passes to children is not the
    /// file's own.
    /// </summary>
    public static string? SharedWith(
        IEnumerable<(string Sid, uint Rights, bool InheritOnly)> allowed
    ) =>
        allowed
            .Where(rule => !rule.InheritOnly && (rule.Rights & ReadOrChange) != 0)
            .Select(rule => Everyone.GetValueOrDefault(rule.Sid))
            .FirstOrDefault(name => name is not null);

    /// <summary>The allow entries of the open file's permissions.</summary>
    [SupportedOSPlatform("windows")]
    public static IEnumerable<(string Sid, uint Rights, bool InheritOnly)> AllowRules(
        FileStream stream
    ) =>
        stream
            .GetAccessControl()
            .GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                typeof(SecurityIdentifier)
            )
            .OfType<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .Select(rule =>
                (
                    rule.IdentityReference.Value,
                    unchecked((uint)rule.FileSystemRights),
                    rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)
                )
            )
            .ToList();
}
