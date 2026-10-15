namespace Trax.Effect.Configuration.ClientExport;

/// <summary>
/// Whether this process was started to export its client manifest (<c>--trax-export-client &lt;path&gt;</c>) rather
/// than to serve. Read from the process's command line, because the database is migrated while services are
/// registered, before a host's <c>Main</c> gets to its arguments: <c>UsePostgres</c> and <c>UseSqlite</c> skip the
/// migration when it is set, so an export never opens a database.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/client-manifest">SDK reference</seealso>
public static class TraxClientExport
{
    /// <summary>The argument that asks for the export, followed by the path to write.</summary>
    public const string Argument = "--trax-export-client";

    /// <summary>The path to write the manifest to, or null when the process was not started to export.</summary>
    public static string? Path { get; } = PathIn(Environment.GetCommandLineArgs());

    /// <summary>Whether the process was started to export its client manifest.</summary>
    public static bool Requested => Path is not null;

    /// <summary>
    /// The path <paramref name="args"/> name after <see cref="Argument"/>, or null when they do not ask for an export.
    /// Throws when the argument is given without a path.
    /// </summary>
    public static string? PathIn(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == Argument)
                return i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[i + 1]
                    : throw new ArgumentException(
                        $"{Argument} needs the path to write the manifest to."
                    );
            if (args[i].StartsWith(Argument + "=", StringComparison.Ordinal))
                return args[i][(Argument.Length + 1)..] is { Length: > 0 } path
                    ? path
                    : throw new ArgumentException(
                        $"{Argument} needs the path to write the manifest to."
                    );
        }
        return null;
    }
}
