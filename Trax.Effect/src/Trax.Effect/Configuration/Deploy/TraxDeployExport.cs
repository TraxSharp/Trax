using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>
/// Whether this process was started to export what it needs from its infrastructure
/// (<c>--trax-export-deploy &lt;path&gt;</c>) rather than to serve. Read from the command line, like the client export,
/// because the database is migrated while services are registered: <c>UsePostgres</c> and <c>UseSqlite</c> open no
/// database when it is set, and a host that serves anyway refuses to start.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/deploy-manifest">SDK reference</seealso>
public static class TraxDeployExport
{
    /// <summary>The argument that asks for the export, followed by the path to write.</summary>
    public const string Argument = "--trax-export-deploy";

    private static readonly Lazy<string?> ProcessPath = new(() =>
        PathIn(Environment.GetCommandLineArgs())
    );

    /// <summary>
    /// The path to write the deploy manifest to, or null when the process was not started to export. Throws
    /// <see cref="ArgumentException"/> when the argument is given without a path.
    /// </summary>
    public static string? Path => ProcessPath.Value;

    /// <summary>Whether the process was started to export its deploy manifest.</summary>
    public static bool Requested => Path is not null;

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
    /// Whether a data provider skips its database for a deploy export. When it does, it also registers the gate that
    /// refuses to serve, so a host that does not take the export through <c>RunTraxAsync</c> never answers on an
    /// unmigrated database.
    /// </summary>
    internal static bool SkipsDatabase(IServiceCollection services, bool? requested = null)
    {
        if (!(requested ?? Requested))
            return false;
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, DeployExportServeGate>()
        );
        return true;
    }

    private static ArgumentException NoPath(string paramName) =>
        new(
            $"The process was started with {Argument} and no path after it. Give the file to write the deploy "
                + $"manifest to, as in '{Argument} deploy.json', or start it without {Argument} to serve."
        );
}
