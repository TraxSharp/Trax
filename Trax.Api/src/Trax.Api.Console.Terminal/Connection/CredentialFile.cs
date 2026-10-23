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
    /// <c>O_RDONLY | O_NONBLOCK | O_NOFOLLOW | O_NOCTTY | O_CLOEXEC</c> for this OS, or null where the values are not
    /// known (Windows among them), and the file is opened as usual.
    /// </summary>
    public static int? OpenFlags()
    {
        if (OperatingSystem.IsLinux())
        {
            // 32-bit ARM numbers O_NOFOLLOW apart from every other Linux .NET runs on.
            var noFollow =
                RuntimeInformation.ProcessArchitecture == Architecture.Arm ? 0x8000 : 0x20000;
            return ReadOnly | 0x800 | noFollow | 0x100 | 0x80000;
        }
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            return ReadOnly | 0x4 | 0x100 | 0x20000 | 0x1000000;
        if (OperatingSystem.IsFreeBSD())
            return ReadOnly | 0x4 | 0x100 | 0x8000 | 0x100000;
        return null;
    }

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
