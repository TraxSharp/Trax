using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>
/// Whether this process migrates the database, and whether it was started only to migrate it
/// (<c>--trax-migrate</c>). Both are read from the process itself, its command line and its environment, rather than
/// from configuration, because <c>UsePostgres</c> and <c>UseSqlite</c> migrate while services are registered, before
/// configuration can be resolved.
/// </summary>
/// <remarks>
/// A deployment runs one process of the new version with <c>--trax-migrate</c> before any other starts, and starts the
/// rest with <c>TRAX_MIGRATIONS=skip</c>, so the image that serves is also the image that migrates. A host started with
/// <c>--trax-migrate</c> migrates even when <c>TRAX_MIGRATIONS=skip</c> is set, and refuses to start at all when its
/// code calls <c>SkipMigrations()</c>, which says this host never migrates.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/trax-migrate">SDK reference</seealso>
public static class TraxMigrate
{
    /// <summary>The argument that asks the process to migrate the database and exit.</summary>
    public const string Argument = "--trax-migrate";

    /// <summary>
    /// The environment variable that turns the migration at registration off. Its one accepted value is
    /// <see cref="SkipValue"/>; any other non-empty value refuses to start.
    /// </summary>
    public const string Variable = "TRAX_MIGRATIONS";

    /// <summary>The value of <see cref="Variable"/> that skips the migration.</summary>
    public const string SkipValue = "skip";

    private static readonly Lazy<bool> ProcessRequested = new(() =>
        RequestedIn(Environment.GetCommandLineArgs())
    );

    /// <summary>Whether the process was started with <see cref="Argument"/>.</summary>
    public static bool Requested => ProcessRequested.Value;

    /// <summary>Whether <paramref name="args"/> contain <see cref="Argument"/>.</summary>
    public static bool RequestedIn(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains(Argument, StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether a data provider migrates while it registers. When the process was started with
    /// <see cref="Argument"/>, it also registers the gate that refuses to serve, so a host that does not exit through
    /// <c>RunTraxAsync</c> after migrating never starts its workers and listeners as well.
    /// </summary>
    internal static bool ShouldMigrate(
        TraxEffectBuilder.TraxEffectBuilder builder,
        bool? requested = null,
        string? variable = null
    )
    {
        var skip = SkipRequested(variable ?? Environment.GetEnvironmentVariable(Variable));

        if (requested ?? Requested)
        {
            if (builder.MigrationsDisabled)
                throw new InvalidOperationException(
                    $"This process was started with {Argument}, but its code calls SkipMigrations(), which says this "
                        + "host never migrates the database. Run the migration from a host that does, or remove "
                        + "SkipMigrations() and set "
                        + $"{Variable}={SkipValue} on the processes that should not migrate."
                );
            builder.ServiceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, MigrateServeGate>()
            );
            return true;
        }

        return !builder.MigrationsDisabled && !skip;
    }

    private static bool SkipRequested(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (string.Equals(value.Trim(), SkipValue, StringComparison.OrdinalIgnoreCase))
            return true;
        throw new InvalidOperationException(
            $"{Variable} is '{value}'. The only value it takes is '{SkipValue}', which stops this host migrating the "
                + $"database when it starts. Unset it to migrate on start."
        );
    }
}

/// <summary>
/// Refuses to start a host that was started with <c>--trax-migrate</c> and is serving instead. A migrate process
/// exits once the schema is current, through <c>RunTraxAsync</c>; one that reaches the host's start would also start
/// its workers and listeners, beside the version it is migrating for.
/// </summary>
internal sealed class MigrateServeGate : IHostedLifecycleService
{
    private bool _checked;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        throw new InvalidOperationException(
            $"This process was started with {TraxMigrate.Argument}, which migrates the database and exits, but the "
                + "host is starting to serve. Call 'await app.RunTraxAsync(args)' in place of 'app.Run()' so the host "
                + $"exits once the schema is current, or start it without {TraxMigrate.Argument}."
        );
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
