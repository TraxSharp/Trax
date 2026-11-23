using System.Globalization;
using System.Text;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy;

/// <summary>
/// Writes a compose stack for a Trax app, for a host that runs rootless podman compose: Postgres, RabbitMQ when a
/// host connects to it, a <c>migrate</c> service that runs the new version once with <c>--trax-migrate</c>, and each
/// deployed service after it with <c>TRAX_MIGRATIONS=skip</c>. Every password, setting and secret comes from the
/// environment and stops <c>compose up</c> when it is missing.
/// </summary>
internal static class ComposeTarget
{
    public const string ComposeFile = "compose.yaml";
    public const string EnvExampleFile = ".env.example";

    /// <summary>The first line of a written compose file, which marks it as one trax may overwrite.</summary>
    public const string Header =
        "# Written by `trax deploy --target compose` from trax.deploy.json. Running it again overwrites this file.";

    /// <summary>The image a service runs: <c>&lt;app&gt;-&lt;service&gt;</c>, as <c>--build</c> names it.</summary>
    public static string ImageName(DeployFile file, string service) => $"{file.App}-{service}";

    public static IReadOnlyList<(string Path, string Content)> Render(
        DeployFile file,
        IReadOnlyDictionary<string, DeployManifest> exports
    )
    {
        Check(file, exports);

        var rabbit = exports.Values.Any(e => e.Transports.Contains("rabbitmq"));
        var migrator =
            file.Services.Keys.FirstOrDefault(k => exports[k].Roles.Contains("api"))
            ?? file.Services.Keys.First();

        var y = new StringBuilder();
        AppendCommon(y, file, rabbit);
        AppendDatabase(y, file);

        if (rabbit)
            AppendRabbitMq(y, file);

        AppendMigrate(y, file, migrator);

        AppendServices(y, file, exports, rabbit);

        y.AppendLine();
        y.AppendLine("volumes:");
        y.AppendLine("  database:");

        return [(ComposeFile, y.ToString()), (EnvExampleFile, EnvExample(file, rabbit))];
    }

    private static void AppendCommon(StringBuilder y, DeployFile file, bool rabbit)
    {
        y.AppendLine(Header);
        y.AppendLine(
            "# For rootless podman: every service runs with userns_mode nomap, which docker compose does not accept."
        );
        y.AppendLine(
            "# Passwords, settings and secrets come from the environment, or from a .env file beside this one: copy"
        );
        y.AppendLine("# .env.example to .env and fill it in. Never commit the .env.");
        y.AppendLine();
        y.AppendLine("# What every Trax service is given.");
        y.AppendLine("x-trax: &trax");
        // The runtime image has no Kerberos library, which Npgsql otherwise probes for on every connection.
        // No password goes into the connection string or a URI, where a ';' or a '/' in it would break them:
        // Npgsql reads PGPASSWORD, and UseRabbitMq(configuration) reads the broker's parts apart.
        y.AppendLine(
            $"  ConnectionStrings__{file.ConnectionStrings.Trax}: Host=database;Database={file.App};Username={file.App};GSS Encryption Mode=Disable"
        );
        y.AppendLine($"  PGPASSWORD: {Required("POSTGRES_PASSWORD")}");
        if (rabbit)
        {
            var broker = file.ConnectionStrings.RabbitMq;
            y.AppendLine($"  {broker}__Host: rabbitmq");
            y.AppendLine($"  {broker}__UserName: {file.App}");
            y.AppendLine($"  {broker}__Password: {Required("RABBITMQ_PASSWORD")}");
        }
        foreach (var name in file.Settings.Concat(file.Secrets).Order(StringComparer.Ordinal))
            y.AppendLine($"  {name}: {Required(name)}");
        y.AppendLine();
        y.AppendLine("services:");
    }

    private static void AppendDatabase(StringBuilder y, DeployFile file)
    {
        y.AppendLine("  database:");
        y.AppendLine("    image: postgres:17");
        y.AppendLine("    environment:");
        y.AppendLine($"      POSTGRES_DB: {file.App}");
        y.AppendLine($"      POSTGRES_USER: {file.App}");
        y.AppendLine($"      POSTGRES_PASSWORD: {Required("POSTGRES_PASSWORD")}");
        y.AppendLine("    volumes:");
        y.AppendLine("      - database:/var/lib/postgresql/data");
        y.AppendLine("    healthcheck:");
        y.AppendLine($"      test: [\"CMD\", \"pg_isready\", \"-U\", \"{file.App}\"]");
        y.AppendLine("      interval: 5s");
        Hardened(y, ServiceHardening.Database);
        y.AppendLine("    restart: unless-stopped");
    }

    private static void AppendRabbitMq(StringBuilder y, DeployFile file)
    {
        y.AppendLine();
        y.AppendLine("  rabbitmq:");
        y.AppendLine("    image: rabbitmq:4");
        y.AppendLine("    environment:");
        y.AppendLine($"      RABBITMQ_DEFAULT_USER: {file.App}");
        y.AppendLine($"      RABBITMQ_DEFAULT_PASS: {Required("RABBITMQ_PASSWORD")}");
        y.AppendLine("    healthcheck:");
        y.AppendLine("      test: [\"CMD\", \"rabbitmq-diagnostics\", \"-q\", \"ping\"]");
        y.AppendLine("      interval: 5s");
        Hardened(y, ServiceHardening.Broker);
        y.AppendLine("    restart: unless-stopped");
    }

    private static void AppendMigrate(StringBuilder y, DeployFile file, string migrator)
    {
        y.AppendLine();
        y.AppendLine(
            "  # The new version migrates the database once, before any service of it starts."
        );
        y.AppendLine("  migrate:");
        y.AppendLine($"    image: {Image(file, migrator)}");
        y.AppendLine($"    command: [\"{TraxMigrate.Argument}\"]");
        y.AppendLine("    environment: *trax");
        Hardened(y, ServiceHardening.Trax(file.Services[migrator]));
        y.AppendLine("    depends_on:");
        y.AppendLine("      database:");
        y.AppendLine("        condition: service_healthy");
        y.AppendLine("    restart: \"no\"");
    }

    private static void AppendServices(
        StringBuilder y,
        DeployFile file,
        IReadOnlyDictionary<string, DeployManifest> exports,
        bool rabbit
    )
    {
        // A port asked for in the file is the service's; the rest take 8080 and on, skipping those.
        var taken = file.Services.Values.Select(s => s.Port).OfType<int>().ToHashSet();
        var nextPort = 8080;
        int FreePort()
        {
            while (!taken.Add(nextPort))
                nextPort++;
            return nextPort;
        }

        foreach (var (name, service) in file.Services)
        {
            var export = exports[name];
            y.AppendLine();
            y.AppendLine($"  {name}:");
            y.AppendLine($"    image: {Image(file, name)}");
            y.AppendLine("    environment:");
            y.AppendLine("      <<: *trax");
            y.AppendLine($"      {TraxMigrate.Variable}: {TraxMigrate.SkipValue}");
            if (Hub(export))
            {
                // Loopback only: whatever puts the app in front of people (a reverse proxy, tailscale serve)
                // decides who reaches it, not the machine's network.
                y.AppendLine("    ports:");
                y.AppendLine(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"      - \"127.0.0.1:{service.Port ?? FreePort()}:8080\""
                );
            }
            Hardened(y, ServiceHardening.Trax(service));
            if (service.Count > 1)
            {
                y.AppendLine("    deploy:");
                y.AppendLine(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"      replicas: {service.Count}"
                );
            }
            y.AppendLine("    depends_on:");
            y.AppendLine("      migrate:");
            y.AppendLine("        condition: service_completed_successfully");
            if (rabbit)
            {
                y.AppendLine("      rabbitmq:");
                y.AppendLine("        condition: service_healthy");
            }
            y.AppendLine("    restart: unless-stopped");
        }
    }

    private static string EnvExample(DeployFile file, bool rabbit)
    {
        var env = new StringBuilder();
        env.AppendLine(
            "# Copy to .env beside compose.yaml and fill in every value. Never commit the .env."
        );
        env.AppendLine("POSTGRES_PASSWORD=");
        if (rabbit)
            env.AppendLine("RABBITMQ_PASSWORD=");
        foreach (var name in file.Settings.Concat(file.Secrets).Order(StringComparer.Ordinal))
            env.AppendLine($"{name}=");
        env.AppendLine("# The tag of the images to run, as trax deploy --build tagged them.");
        env.AppendLine("TRAX_IMAGE_TAG=latest");

        return env.ToString();
    }

    private static string Image(DeployFile file, string service) =>
        $"{ImageName(file, service)}:${{TRAX_IMAGE_TAG:-latest}}";

    /// <summary>
    /// Renders <paramref name="hardening"/>. <c>nomap</c> leaves the deploying user's own uid out of the container,
    /// so an escape does not land as them; <c>auto</c> would give each service a range of its own, but fails on a
    /// host whose existing containers already hold the user's whole subordinate range, which is the default.
    /// </summary>
    private static void Hardened(StringBuilder y, ServiceHardening hardening)
    {
        y.AppendLine("    cap_drop:");
        y.AppendLine("      - ALL");
        if (hardening.AddCapabilities.Count > 0)
        {
            y.AppendLine("    cap_add:");
            foreach (var capability in hardening.AddCapabilities)
                y.AppendLine($"      - {capability}");
        }
        y.AppendLine("    security_opt:");
        y.AppendLine("      - no-new-privileges:true");
        y.AppendLine("    userns_mode: nomap");
        y.AppendLine(CultureInfo.InvariantCulture, $"    pids_limit: {ServiceHardening.Pids}");
        y.AppendLine(CultureInfo.InvariantCulture, $"    mem_limit: {hardening.MemoryMiB}m");
    }

    private static string Required(string name) => $"${{{name}:?set {name}}}";

    /// <summary>The roles a service runs as one copy of, until hosts hold leases on their runs.</summary>
    private static readonly string[] SingleRoles = ["api", "scheduler"];

    /// <summary>
    /// Whether compose publishes a port for the service: a hub, which serves HTTP and runs the api or scheduler role,
    /// so runs as one copy. A port on a service with several copies would be bound once per copy.
    /// </summary>
    private static bool Hub(DeployManifest export) =>
        (export.Endpoints.GraphQL is not null || export.Endpoints.Health is not null)
        && export.Roles.Intersect(SingleRoles, StringComparer.Ordinal).Any();

    /// <summary>Refuses a deployment compose cannot give what the hosts said they need.</summary>
    internal static void Check(DeployFile file, IReadOnlyDictionary<string, DeployManifest> exports)
    {
        CheckHosts(file, exports, name => file.Services[name].Count);
        foreach (var (name, service) in file.Services)
            if (service.Port is { } port && !Hub(exports[name]))
                throw new InvalidOperationException(
                    $"Service '{name}' has port {port.ToString(System.Globalization.CultureInfo.InvariantCulture)}, but compose publishes a port only for a service that serves "
                        + "HTTP and runs the api or scheduler role. Remove the port."
                );
    }

    /// <summary>
    /// Refuses what no deployment can give the hosts: runs kept anywhere but Postgres, or more than one copy of a
    /// service running the api or scheduler role. <paramref name="count"/> is how many copies each service runs.
    /// </summary>
    internal static void CheckHosts(
        DeployFile file,
        IReadOnlyDictionary<string, DeployManifest> exports,
        Func<string, int> count
    )
    {
        foreach (var name in file.Services.Keys)
        {
            var copies = count(name);
            var export = exports[name];
            switch (export.Database?.Provider)
            {
                case "postgres":
                    break;
                case null:
                    throw new InvalidOperationException(
                        $"Service '{name}' reports no data provider. A deployed Trax host keeps its runs in "
                            + "Postgres: call UsePostgres in AddEffects."
                    );
                default:
                    throw new InvalidOperationException(
                        $"Service '{name}' keeps its runs in {export.Database.Provider}. A deployment gives it "
                            + $"Postgres, under ConnectionStrings:{file.ConnectionStrings.Trax}: call UsePostgres "
                            + "with that connection string when it is set."
                    );
            }
            var single = export.Roles.Intersect(SingleRoles, StringComparer.Ordinal).ToList();
            if (copies > 1 && single.Count > 0)
                throw new InvalidOperationException(
                    $"Service '{name}' runs the {string.Join(" and ", single)} role and has count "
                        + string.Create(
                            CultureInfo.InvariantCulture,
                            $"{copies}. It runs as one until hosts hold leases on their runs: a second copy "
                        )
                        + "restarting fails the runs live on the first. Scale its workers instead."
                );
        }
    }
}
