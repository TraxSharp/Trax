using System.Globalization;
using System.Text;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy;

/// <summary>
/// Writes a compose stack for a Trax app, for a host that runs rootless podman compose (or docker compose, for an
/// environment whose engine is docker): Postgres, RabbitMQ when a
/// host connects to it, a <c>migrate</c> service that runs the new version once with <c>--trax-migrate</c>, a
/// <c>drain</c> service after it that stops every running host taking new work and waits for what is busy
/// (<c>--trax-drain</c>, Trax.Docs/adr/0065), each deployed service after that with <c>TRAX_MIGRATIONS=skip</c>, and a
/// <c>resume</c> service once they have started (<c>--trax-resume</c>). Every password, setting and secret comes from the
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

    /// <summary>
    /// Renders the stack for <paramref name="engine"/>: <c>podman</c> gives every service a user namespace that leaves
    /// the deploying user's uid out (<c>userns_mode: nomap</c>), which docker compose does not accept, so <c>docker</c>
    /// omits it.
    /// </summary>
    public static IReadOnlyList<(string Path, string Content)> Render(
        DeployFile file,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string engine = DeployEnvironment.Podman,
        IReadOnlyDictionary<string, DeployLimits>? limits = null,
        TimeSpan? drainTimeout = null
    )
    {
        limits ??= new Dictionary<string, DeployLimits>(StringComparer.Ordinal);
        if (engine is not (DeployEnvironment.Podman or DeployEnvironment.Docker))
            throw new InvalidOperationException($"Engine '{engine}' is not podman or docker.");
        var podman = string.Equals(engine, DeployEnvironment.Podman, StringComparison.Ordinal);
        Check(file, exports);

        var rabbit = exports.Values.Any(e => e.Transports.Contains("rabbitmq"));
        if (!rabbit && limits.ContainsKey(DeployFile.BackingBroker))
            throw new InvalidOperationException(
                $"limits names {DeployFile.BackingBroker}, and no host uses RabbitMQ, so the stack runs none."
            );
        var migrator = Migrator(file, exports);

        var y = new StringBuilder();
        AppendCommon(y, file, rabbit, podman);
        AppendDatabase(y, file, podman, limits.GetValueOrDefault(DeployFile.BackingDatabase));

        if (rabbit)
            AppendRabbitMq(y, file, podman, limits.GetValueOrDefault(DeployFile.BackingBroker));

        var egress = file.Services.Values.Any(s => s.Egress is not null)
            ? new Egress(DeployEgress.Image(file), OwnServices(rabbit))
            : null;

        AppendMigrate(y, file, migrator, podman, egress);

        AppendDrain(y, file, migrator, podman, drainTimeout ?? TraxDrain.DefaultTimeout, egress);

        AppendServices(y, file, exports, rabbit, podman, egress);

        AppendResume(y, file, migrator, podman, egress);

        y.AppendLine();
        y.AppendLine("volumes:");
        y.AppendLine("  database:");

        return [(ComposeFile, y.ToString()), (EnvExampleFile, EnvExample(file, rabbit))];
    }

    private static void AppendCommon(StringBuilder y, DeployFile file, bool rabbit, bool podman)
    {
        y.AppendLine(Header);
        y.AppendLine(
            podman
                ? "# For rootless podman: every service runs with userns_mode nomap, which docker compose does not accept."
                : "# For docker compose: no userns_mode, which docker accepts only as host, so no service has a user namespace of its own."
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

    private static void AppendDatabase(
        StringBuilder y,
        DeployFile file,
        bool podman,
        DeployLimits? limits
    )
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
        Hardened(y, ServiceHardening.Database(limits), podman);
        y.AppendLine("    restart: unless-stopped");
    }

    private static void AppendRabbitMq(
        StringBuilder y,
        DeployFile file,
        bool podman,
        DeployLimits? limits
    )
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
        Hardened(y, ServiceHardening.Broker(limits), podman);
        y.AppendLine("    restart: unless-stopped");
    }

    private static void AppendMigrate(
        StringBuilder y,
        DeployFile file,
        string migrator,
        bool podman,
        Egress? egress
    )
    {
        var held = egress is not null && file.Services[migrator].Egress is not null;
        y.AppendLine();
        y.AppendLine(
            "  # The new version migrates the database once, before any service of it starts."
        );
        y.AppendLine("  migrate:");
        y.AppendLine($"    image: {Image(file, migrator)}");
        y.AppendLine($"    command: [\"{TraxMigrate.Argument}\"]");
        y.AppendLine("    environment: *trax");
        // The migration runs the migrator's code, so it is held to the migrator's egress.
        if (held)
            y.AppendLine($"    network_mode: \"service:{SidecarName(migrator)}\"");
        Hardened(y, ServiceHardening.Trax(file.Services[migrator]), podman);
        y.AppendLine("    depends_on:");
        y.AppendLine("      database:");
        y.AppendLine("        condition: service_healthy");
        if (held)
        {
            y.AppendLine($"      {SidecarName(migrator)}:");
            y.AppendLine("        condition: service_healthy");
        }
        y.AppendLine("    restart: \"no\"");
    }

    /// <summary>The egress image, and the deployment's own services every held service still reaches.</summary>
    private sealed record Egress(string Image, IReadOnlyList<string> Own);

    /// <summary>The services trax runs beside the app that a held service still reaches: Postgres, and RabbitMQ.</summary>
    private static List<string> OwnServices(bool rabbit)
    {
        var own = new List<string> { DeployEgress.Own("database", 5432) };
        if (rabbit)
            own.Add(DeployEgress.Own("rabbitmq", 5672));
        return own;
    }

    /// <summary>A held service's egress sidecar: <c>&lt;service&gt;-egress</c>.</summary>
    internal static string SidecarName(string service) =>
        $"{service}-{DeployEgress.SidecarContainer}";

    /// <summary>
    /// The sidecar that holds <paramref name="name"/> to its egress. Compose has no init container, so it runs
    /// <c>run</c>: it applies the rules as root with NET_ADMIN, gives up root and every capability, and serves; the
    /// service joins its network namespace and starts once it is ready. It owns the namespace, so the service's port
    /// is published here.
    /// </summary>
    private static void AppendSidecar(
        StringBuilder y,
        string name,
        DeployService service,
        Egress egress,
        bool podman,
        int? port
    )
    {
        y.AppendLine();
        y.AppendLine(
            $"  # Holds {name} to the domains it names; {name} joins this service's network namespace."
        );
        y.AppendLine($"  {SidecarName(name)}:");
        y.AppendLine($"    image: {egress.Image}");
        // Root only to apply the rules; the process gives it up, and every capability, before it serves.
        y.AppendLine("    user: \"0:0\"");
        y.AppendLine($"    command: [\"{DeployEgress.RunCommand}\"]");
        y.AppendLine("    environment:");
        y.AppendLine(
            $"      {DeployEgress.AllowVariable}: \"{DeployEgress.Allow(service.Egress ?? [], egress.Own)}\""
        );
        if (port is { } p)
        {
            y.AppendLine("    ports:");
            y.AppendLine(CultureInfo.InvariantCulture, $"      - \"127.0.0.1:{p}:8080\"");
        }
        y.AppendLine("    healthcheck:");
        y.AppendLine(
            $"      test: [\"CMD\", \"{DeployEgress.Binary}\", \"{DeployEgress.ReadyCommand}\"]"
        );
        y.AppendLine("      interval: 2s");
        y.AppendLine("      retries: 30");
        Hardened(y, EgressHardening, podman);
        y.AppendLine("    restart: unless-stopped");
    }

    /// <summary>
    /// The sidecar's limits: NET_ADMIN to apply the rules, SETUID and SETGID to give up root after, and nothing it
    /// keeps once it serves.
    /// </summary>
    private static readonly ServiceHardening EgressHardening = new(
        ["NET_ADMIN", "SETGID", "SETUID"],
        MemoryMiB: null,
        Pids: 256,
        Cpus: null,
        ReadOnlyRoot: true
    );

    private static void AppendDrain(
        StringBuilder y,
        DeployFile file,
        string migrator,
        bool podman,
        TimeSpan timeout,
        Egress? egress
    )
    {
        var held = egress is not null && file.Services[migrator].Egress is not null;
        y.AppendLine();
        y.AppendLine(
            "  # Before any service is replaced: every running host stops taking new work, and this waits for what is"
        );
        y.AppendLine(
            "  # busy. Past the timeout it ends the drain and fails, naming what is busy, and nothing is replaced;"
        );
        y.AppendLine("  # TRAX_DRAIN_FORCE=true replaces the services anyway.");
        y.AppendLine("  drain:");
        y.AppendLine($"    image: {Image(file, migrator)}");
        y.AppendLine(
            CultureInfo.InvariantCulture,
            $"    command: [\"{TraxDrain.Argument}\", \"{timeout:c}\"]"
        );
        y.AppendLine("    environment:");
        y.AppendLine("      <<: *trax");
        y.AppendLine($"      {TraxMigrate.Variable}: {TraxMigrate.SkipValue}");
        y.AppendLine($"      {TraxDrain.ForceVariable}: ${{{TraxDrain.ForceVariable}:-false}}");
        // The drain runs the migrator's code, so it is held to the migrator's egress, as the migration is.
        if (held)
            y.AppendLine($"    network_mode: \"service:{SidecarName(migrator)}\"");
        Hardened(y, ServiceHardening.Trax(file.Services[migrator]), podman);
        y.AppendLine("    depends_on:");
        y.AppendLine("      migrate:");
        y.AppendLine("        condition: service_completed_successfully");
        y.AppendLine("    restart: \"no\"");
    }

    private static void AppendResume(
        StringBuilder y,
        DeployFile file,
        string migrator,
        bool podman,
        Egress? egress
    )
    {
        var held = egress is not null && file.Services[migrator].Egress is not null;
        y.AppendLine();
        y.AppendLine(
            "  # Once every service has started, the drain ends and every host takes work again. A scheduler host that"
        );
        y.AppendLine("  # starts ends it too; this covers a release that restarted none.");
        y.AppendLine("  resume:");
        y.AppendLine($"    image: {Image(file, migrator)}");
        y.AppendLine($"    command: [\"{TraxDrain.ResumeArgument}\"]");
        y.AppendLine("    environment:");
        y.AppendLine("      <<: *trax");
        y.AppendLine($"      {TraxMigrate.Variable}: {TraxMigrate.SkipValue}");
        if (held)
            y.AppendLine($"    network_mode: \"service:{SidecarName(migrator)}\"");
        Hardened(y, ServiceHardening.Trax(file.Services[migrator]), podman);
        y.AppendLine("    depends_on:");
        foreach (var name in file.Services.Keys)
        {
            y.AppendLine($"      {name}:");
            y.AppendLine("        condition: service_started");
        }
        y.AppendLine("    restart: \"no\"");
    }

    private static void AppendServices(
        StringBuilder y,
        DeployFile file,
        IReadOnlyDictionary<string, DeployManifest> exports,
        bool rabbit,
        bool podman,
        Egress? egress
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
            // Loopback only: whatever puts the app in front of people (a reverse proxy, tailscale serve) decides who
            // reaches it, not the machine's network.
            int? port = Hub(export) ? service.Port ?? FreePort() : null;
            var sidecar = service.Egress is not null ? egress : null;
            var held = sidecar is not null;
            if (sidecar is not null)
                AppendSidecar(y, name, service, sidecar, podman, port);
            y.AppendLine();
            y.AppendLine($"  {name}:");
            y.AppendLine($"    image: {Image(file, name)}");
            y.AppendLine("    environment:");
            y.AppendLine("      <<: *trax");
            y.AppendLine($"      {TraxMigrate.Variable}: {TraxMigrate.SkipValue}");
            if (held)
                y.AppendLine($"    network_mode: \"service:{SidecarName(name)}\"");
            else if (port is { } p)
            {
                y.AppendLine("    ports:");
                y.AppendLine(CultureInfo.InvariantCulture, $"      - \"127.0.0.1:{p}:8080\"");
            }
            Hardened(y, ServiceHardening.Trax(service), podman);
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
            if (held)
            {
                y.AppendLine($"      {SidecarName(name)}:");
                y.AppendLine("        condition: service_healthy");
            }
            y.AppendLine("      drain:");
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
        env.AppendLine(
            "# true replaces the services even when work is still busy when the drain times out. Leave it false, and"
        );
        env.AppendLine(
            "# set it for one release when you mean it: TRAX_DRAIN_FORCE=true podman compose up -d migrate drain."
        );
        env.AppendLine($"{TraxDrain.ForceVariable}=false");
        env.AppendLine("# The tag of the images to run, as trax deploy --build tagged them.");
        env.AppendLine("TRAX_IMAGE_TAG=latest");

        return env.ToString();
    }

    /// <summary>The service whose image runs the migration: the first that runs the api role, else the first.</summary>
    internal static string Migrator(
        DeployFile file,
        IReadOnlyDictionary<string, DeployManifest> exports
    ) =>
        file.Services.Keys.FirstOrDefault(k => exports[k].Roles.Contains("api"))
        ?? file.Services.Keys.First();

    private static string Image(DeployFile file, string service) =>
        $"{ImageName(file, service)}:${{TRAX_IMAGE_TAG:-latest}}";

    /// <summary>
    /// Renders <paramref name="hardening"/>. <c>nomap</c> leaves the deploying user's own uid out of the container,
    /// so an escape does not land as them; <c>auto</c> would give each service a range of its own, but fails on a
    /// host whose existing containers already hold the user's whole subordinate range, which is the default. Docker
    /// compose accepts only <c>host</c>, so a docker stack sets none.
    /// </summary>
    private static void Hardened(StringBuilder y, ServiceHardening hardening, bool podman)
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
        if (podman)
            y.AppendLine("    userns_mode: nomap");
        if (hardening.ReadOnlyRoot)
        {
            // A Trax host writes nothing outside /tmp; anything else it tries to write fails instead of persisting.
            y.AppendLine("    read_only: true");
            y.AppendLine("    tmpfs:");
            y.AppendLine("      - /tmp");
        }
        if (hardening.Pids is { } pids)
            y.AppendLine(CultureInfo.InvariantCulture, $"    pids_limit: {pids}");
        if (hardening.Cpus is { } cpus)
            y.AppendLine(CultureInfo.InvariantCulture, $"    cpus: {cpus}");
        if (hardening.MemoryMiB is { } memory)
        {
            y.AppendLine(CultureInfo.InvariantCulture, $"    mem_limit: {memory}m");
            // Without it the runtime allows as much swap again, so a copy could hold twice its memory before the kill.
            y.AppendLine(CultureInfo.InvariantCulture, $"    memswap_limit: {memory}m");
        }
    }

    private static string Required(string name) => $"${{{name}:?set {name}}}";

    /// <summary>
    /// The roles a service runs as one copy of, until hosts hold leases on their runs: a copy that restarts fails the
    /// runs live on the others in its startup recovery. During a deploy the old and new copy of a hub may overlap,
    /// which is safe only because the deploy drained first (Trax.Cli/docs/adr/0007).
    /// </summary>
    internal static readonly string[] SingleRoles = ["api", "scheduler"];

    /// <summary>
    /// Whether compose publishes a port for the service: a hub, which serves HTTP and runs the api or scheduler role,
    /// so runs as one copy. A port on a service with several copies would be bound once per copy.
    /// </summary>
    internal static bool Hub(DeployManifest export) =>
        (export.Endpoints.GraphQL is not null || export.Endpoints.Health is not null)
        && export.Roles.Intersect(SingleRoles, StringComparer.Ordinal).Any();

    /// <summary>Refuses a deployment compose cannot give what the hosts said they need.</summary>
    internal static void Check(DeployFile file, IReadOnlyDictionary<string, DeployManifest> exports)
    {
        CheckHosts(file, exports, name => file.Services[name].Count);
        TailnetSignIn.Refuse(
            exports,
            "a compose stack publishes its hub on 127.0.0.1, where every process on the host, and a browser through "
                + "DNS rebinding, can send those headers."
        );
        foreach (var (name, service) in file.Services)
        {
            if (service.Port is { } port && !Hub(exports[name]))
                throw new InvalidOperationException(
                    $"Service '{name}' has port {port.ToString(System.Globalization.CultureInfo.InvariantCulture)}, but compose publishes a port only for a service that serves "
                        + "HTTP and runs the api or scheduler role. Remove the port."
                );
            if (service.Egress is not null && service.Count > 1)
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Service '{name}' names egress and has count {service.Count}. Under compose its copies "
                    )
                        + "would share one egress sidecar's network namespace, and with it one set of ports, so "
                        + "a held service runs one copy. Give it count 1, or deploy it with the kubernetes target, "
                        + "where each copy has a sidecar of its own."
                );
        }
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
