using System.Text;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy;

/// <summary>
/// Writes a compose stack for a Trax app, for any host that runs docker or podman compose: Postgres, RabbitMQ when a
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
        y.AppendLine(Header);
        y.AppendLine(
            "# Passwords, settings and secrets come from the environment, or from a .env file beside this one: copy"
        );
        y.AppendLine("# .env.example to .env and fill it in. Never commit the .env.");
        y.AppendLine();
        y.AppendLine("# What every Trax service is given.");
        y.AppendLine("x-trax: &trax");
        // The runtime image has no Kerberos library, which Npgsql otherwise probes for on every connection.
        y.AppendLine(
            $"  ConnectionStrings__{file.ConnectionStrings.Trax}: Host=database;Database={file.App};Username={file.App};Password={Required("POSTGRES_PASSWORD")};GSS Encryption Mode=Disable"
        );
        if (rabbit)
            y.AppendLine(
                $"  ConnectionStrings__{file.ConnectionStrings.RabbitMq}: amqp://{file.App}:{Required("RABBITMQ_PASSWORD")}@rabbitmq:5672"
            );
        foreach (var name in file.Settings.Concat(file.Secrets).Order(StringComparer.Ordinal))
            y.AppendLine($"  {name}: {Required(name)}");
        y.AppendLine();
        y.AppendLine("services:");

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
        y.AppendLine("    security_opt:");
        y.AppendLine("      - no-new-privileges:true");
        y.AppendLine("    restart: unless-stopped");

        if (rabbit)
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
            y.AppendLine("    security_opt:");
            y.AppendLine("      - no-new-privileges:true");
            y.AppendLine("    restart: unless-stopped");
        }

        y.AppendLine();
        y.AppendLine(
            "  # The new version migrates the database once, before any service of it starts."
        );
        y.AppendLine("  migrate:");
        y.AppendLine($"    image: {Image(file, migrator)}");
        y.AppendLine($"    command: [\"{TraxMigrate.Argument}\"]");
        y.AppendLine("    environment: *trax");
        Hardened(y);
        y.AppendLine("    depends_on:");
        y.AppendLine("      database:");
        y.AppendLine("        condition: service_healthy");
        y.AppendLine("    restart: \"no\"");

        var port = 8080;
        foreach (var (name, service) in file.Services)
        {
            var export = exports[name];
            y.AppendLine();
            y.AppendLine($"  {name}:");
            y.AppendLine($"    image: {Image(file, name)}");
            y.AppendLine("    environment:");
            y.AppendLine("      <<: *trax");
            y.AppendLine($"      {TraxMigrate.Variable}: {TraxMigrate.SkipValue}");
            if (export.Endpoints.GraphQL is not null || export.Endpoints.Health is not null)
            {
                // Loopback only: whatever puts the app in front of people (a reverse proxy, tailscale serve)
                // decides who reaches it, not the machine's network.
                y.AppendLine("    ports:");
                y.AppendLine($"      - \"127.0.0.1:{service.Port ?? port}:8080\"");
                port = (service.Port ?? port) + 1;
            }
            Hardened(y);
            if (service.Count > 1)
            {
                y.AppendLine("    deploy:");
                y.AppendLine($"      replicas: {service.Count}");
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

        y.AppendLine();
        y.AppendLine("volumes:");
        y.AppendLine("  database:");

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

        return [(ComposeFile, y.ToString()), (EnvExampleFile, env.ToString())];
    }

    private static string Image(DeployFile file, string service) =>
        $"{ImageName(file, service)}:${{TRAX_IMAGE_TAG:-latest}}";

    /// <summary>A Trax service needs no Linux capability and never gains one.</summary>
    private static void Hardened(StringBuilder y)
    {
        y.AppendLine("    cap_drop:");
        y.AppendLine("      - ALL");
        y.AppendLine("    security_opt:");
        y.AppendLine("      - no-new-privileges:true");
    }

    private static string Required(string name) => $"${{{name}:?set {name}}}";

    /// <summary>Refuses a deployment compose cannot give what the hosts said they need.</summary>
    internal static void Check(
        DeployFile file,
        IReadOnlyDictionary<string, DeployManifest> exports
    ) => CheckHosts(file, exports, name => file.Services[name].Count);

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
            var single = export.Roles.Intersect(["api", "scheduler"]).ToList();
            if (copies > 1 && single.Count > 0)
                throw new InvalidOperationException(
                    $"Service '{name}' runs the {string.Join(" and ", single)} role and has count "
                        + $"{copies}. It runs as one until hosts hold leases on their runs: a second copy "
                        + "restarting fails the runs live on the first. Scale its workers instead."
                );
        }
    }
}
