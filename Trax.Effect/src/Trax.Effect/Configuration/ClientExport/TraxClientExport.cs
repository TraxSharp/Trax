using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Trax.Effect.Configuration.ClientExport;

/// <summary>
/// Whether this process was started to export its client manifest (<c>--trax-export-client &lt;path&gt;</c>) rather
/// than to serve. Read from the process's command line, because the database is migrated while services are
/// registered, before a host's <c>Main</c> gets to its arguments: <c>UsePostgres</c> and <c>UseSqlite</c> skip the
/// migration when it is set, so an export never opens a database. A host started with it that serves anyway, because it
/// calls <c>app.Run()</c> rather than <c>RunTraxAsync</c>, refuses to start, since its database was not migrated.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/client-manifest">SDK reference</seealso>
public static class TraxClientExport
{
    /// <summary>The argument that asks for the export, followed by the path to write.</summary>
    public const string Argument = "--trax-export-client";

    private static readonly ClientExportFlag Process = new(Environment.GetCommandLineArgs);

    /// <summary>
    /// The path to write the manifest to, or null when the process was not started to export. Read on first use;
    /// throws <see cref="ArgumentException"/> when the argument is given without a path.
    /// </summary>
    public static string? Path => Process.Path;

    /// <summary>
    /// Whether the process was started to export its client manifest. Throws <see cref="ArgumentException"/> when the
    /// argument is given without a path.
    /// </summary>
    public static bool Requested => Process.Requested;

    /// <summary>
    /// The path <paramref name="args"/> name after <see cref="Argument"/>, or null when they do not ask for an export.
    /// Throws <see cref="ArgumentException"/> when the argument is given without a path.
    /// </summary>
    public static string? PathIn(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], Argument, StringComparison.Ordinal))
                return i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[i + 1]
                    : throw NoPath(nameof(args));
            if (args[i].StartsWith(Argument + "=", StringComparison.Ordinal))
                return args[i][(Argument.Length + 1)..] is { Length: > 0 } path
                    ? path
                    : throw NoPath(nameof(args));
        }
        return null;
    }

    /// <summary>
    /// Whether a data provider skips its database for an export. When it does, it also registers the gate that refuses
    /// to serve, so a host that does not take the export through <c>RunTraxAsync</c> never answers on an unmigrated
    /// database.
    /// </summary>
    internal static bool SkipsDatabase(IServiceCollection services, ClientExportFlag? flag = null)
    {
        if (!(flag ?? Process).Requested)
            return false;
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ClientExportServeGate>()
        );
        return true;
    }

    private static ArgumentException NoPath(string paramName) =>
        new(
            $"The process was started with {Argument} and no path after it. Give the file to write the client "
                + $"manifest to, as in '{Argument} client.json', or start it without {Argument} to serve.",
            paramName
        );
}
