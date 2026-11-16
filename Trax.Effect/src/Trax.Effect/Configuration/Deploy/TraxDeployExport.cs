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
            if (args[i] == Argument)
                return i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[i + 1]
                    : throw NoPath();
            if (args[i].StartsWith(Argument + "=", StringComparison.Ordinal))
                return args[i][(Argument.Length + 1)..] is { Length: > 0 } path
                    ? path
                    : throw NoPath();
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

    private static ArgumentException NoPath() =>
        new(
            $"The process was started with {Argument} and no path after it. Give the file to write the deploy "
                + $"manifest to, as in '{Argument} deploy.json', or start it without {Argument} to serve."
        );
}

/// <summary>
/// One thing a host needs from where it is deployed, recorded by the registration that adds it: its data provider, a
/// role (<c>scheduler</c>, <c>worker</c>, <c>job-runner</c>), a transport. Collected into the
/// <see cref="DeployManifest"/> a deploy export writes.
/// </summary>
internal sealed record DeployFact(string Kind, string Value)
{
    public const string Database = "database";
    public const string MigratesOnStart = "migrates-on-start";
    public const string Role = "role";
    public const string Transport = "transport";

    /// <summary>Records a fact. Duplicates are harmless: the manifest keeps one of each.</summary>
    public static void Add(IServiceCollection services, string kind, string value) =>
        services.AddSingleton(new DeployFact(kind, value));
}

/// <summary>
/// Adds what only the built host knows (its endpoints, its authentication schemes) to the deploy manifest. Registered
/// by the packages that map endpoints.
/// </summary>
internal interface IDeployManifestSource
{
    /// <param name="manifest">The manifest being built.</param>
    /// <param name="services">The host's services.</param>
    /// <param name="host">The host itself, which on a web host is where its endpoints are mapped.</param>
    /// <param name="cancellationToken">Stops the export.</param>
    Task DescribeAsync(
        DeployManifest manifest,
        IServiceProvider services,
        IHost? host,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Refuses to start a host that was started to export its deploy manifest and is serving instead. Its data provider
/// opened no database for the export, so serving would answer on an unmigrated one.
/// </summary>
internal sealed class DeployExportServeGate : IHostedLifecycleService
{
    private bool _checked;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        throw new InvalidOperationException(
            $"This process was started with {TraxDeployExport.Argument}, so its database was not migrated, but the "
                + "host is starting to serve. Call 'await app.RunTraxAsync(args)' in place of 'app.Run()' so the "
                + $"export is written and the host exits, or start it without {TraxDeployExport.Argument}."
        );
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
