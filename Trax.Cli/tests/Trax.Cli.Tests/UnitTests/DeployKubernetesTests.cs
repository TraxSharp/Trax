using System.Text.RegularExpressions;
using AwesomeAssertions;
using k8s.Models;
using Trax.Cli.Commands;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// An environment with <c>"target": "kubernetes"</c> becomes one Deployment per service (a hub at one copy that rolls
/// with its next copy beside it, workers rolling at their count), a ClusterIP Service per hub, and a migrate Job that
/// runs before them, every container under the same hardening compose renders for that service, and no secret value anywhere:
/// each comes from a Secret by reference. Postgres and RabbitMQ run in the cluster only off production. The file's
/// environments each name their target; a version 1 file's are aws. Holds the Kubernetes target to
/// <c>Trax.Docs/adr/0062</c>.
/// </summary>
public partial class DeployKubernetesTests
{
    private const string Tag = "20261117-120000-abcdef123456-dirty";

    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-k8s-{Guid.NewGuid():N}");
        foreach (
            var project in new[]
            {
                "Orders/Orders.csproj",
                "Orders.Worker/Orders.Worker.csproj",
                "Orders.Reports/Orders.Reports.csproj",
            }
        )
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_dir, project)).NotNull());
            File.WriteAllText(Path.Combine(_dir, project), "<Project />");
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    internal static DeployFile File_(string profile = DeployEnvironment.Try)
    {
        var file = new DeployFile
        {
            App = "orders",
            Services =
            {
                ["hub"] = new DeployService { Project = "Orders/Orders.csproj" },
                ["worker"] = new DeployService
                {
                    Project = "Orders.Worker/Orders.Worker.csproj",
                    Count = 2,
                    Memory = 2048,
                },
                ["reports"] = new DeployService
                {
                    Project = "Orders.Reports/Orders.Reports.csproj",
                },
            },
            Settings = ["Auth__Authority"],
            Secrets = ["Stripe__ApiKey"],
        };
        file.Environments["staging"] = new DeployEnvironment
        {
            Target = DeployEnvironment.Kubernetes,
            Profile = profile,
            Database = string.Equals(profile, DeployEnvironment.Try, StringComparison.Ordinal)
                ? new DeployEnvironmentDatabase { InCluster = true }
                : new DeployEnvironmentDatabase { Secret = "orders-db", Key = "connection" },
            Settings = { ["Auth__Authority"] = "https://issuer.example.com" },
            Counts = { ["worker"] = 3 },
        };
        return file;
    }

    internal static Dictionary<string, DeployManifest> Exports(bool rabbit = false)
    {
        string[] transports = rabbit ? ["rabbitmq"] : [];
        return new(StringComparer.Ordinal)
        {
            ["hub"] = new DeployManifest
            {
                Roles = ["api", "scheduler"],
                Database = new DeployDatabase { Provider = "postgres", MigratesOnStart = true },
                Transports = [.. transports],
                Endpoints = new DeployEndpoints
                {
                    GraphQL = "/trax/graphql",
                    Health = "/trax/health",
                },
            },
            ["worker"] = new DeployManifest
            {
                Roles = ["worker"],
                Database = new DeployDatabase { Provider = "postgres", MigratesOnStart = true },
                Transports = [.. transports],
            },
            ["reports"] = new DeployManifest
            {
                Roles = ["worker"],
                Database = new DeployDatabase { Provider = "postgres", MigratesOnStart = true },
            },
        };
    }

    private static KubernetesManifests Render(
        DeployFile? file = null,
        Dictionary<string, DeployManifest>? exports = null
    ) => KubernetesTarget.Render(file ?? File_(), "staging", exports ?? Exports(), Tag);

    private static Dictionary<string, V1Deployment> Deployments(KubernetesManifests m) =>
        m
            .Services.OfType<V1Deployment>()
            .ToDictionary(d => d.Metadata.Name, StringComparer.Ordinal);

    private static IEnumerable<V1PodTemplateSpec> Pods(KubernetesManifests m) =>
        m
            .All.Select(o =>
                o switch
                {
                    V1Deployment d => d.Spec.Template,
                    V1StatefulSet s => s.Spec.Template,
                    V1Job j => j.Spec.Template,
                    _ => null,
                }
            )
            .OfType<V1PodTemplateSpec>();

    [Test]
    public void A_hub_and_two_workers_become_a_Deployment_each_and_a_migrate_Job()
    {
        var m = Render();

        var deployments = Deployments(m);
        deployments.Keys.Should().BeEquivalentTo(["hub", "worker", "reports"]);
        deployments["hub"].Spec.Replicas.Should().Be(1);
        deployments["hub"]
            .Spec.Strategy.Type.Should()
            .Be("RollingUpdate", "the new hub starts beside the old one, after the deploy drained");
        deployments["worker"].Spec.Strategy.Type.Should().Be("RollingUpdate");
        deployments["worker"]
            .Spec.Replicas.Should()
            .Be(3, "the environment's count overrides the file's");
        deployments["reports"].Spec.Replicas.Should().Be(1);
        deployments["reports"].Spec.Strategy.Type.Should().Be("RollingUpdate");

        m.Migrate.Metadata.Name.Should().Be("orders-migrate-20261117-120000-abcdef123456-dirty");
        m.Migrate.Spec.BackoffLimit.Should().Be(0);
        m.Migrate.Spec.TtlSecondsAfterFinished.Should().Be(86_400);
        m.Migrate.Spec.Template.Spec.RestartPolicy.Should().Be("Never");
        var job = m.Migrate.Spec.Template.Spec.Containers.Single();
        job.Args.Should().Equal(TraxMigrate.Argument);
        job.Image.Should().Be($"orders-hub:{Tag}", "the service running the api migrates");

        m.NamespaceName.Should().Be("orders-staging");
        m.All.Select(o => o.Kind)
            .Should()
            .Equal(
                "Namespace",
                "NetworkPolicy",
                "NetworkPolicy",
                "Service",
                "StatefulSet",
                "Job",
                "Job",
                "Service",
                "Deployment",
                "Deployment",
                "Deployment",
                "Job"
            );
    }

    [Test]
    public void Every_service_skips_migrations_and_the_job_runs_them()
    {
        var m = Render(exports: Exports(rabbit: true));

        foreach (var deployment in Deployments(m).Values)
            deployment
                .Spec.Template.Spec.Containers.Single()
                .Env.Should()
                .ContainSingle(e =>
                    e.Name == TraxMigrate.Variable && e.Value == TraxMigrate.SkipValue
                );
        m.Migrate.Spec.Template.Spec.Containers.Single()
            .Env.Should()
            .NotContain(e => e.Name == TraxMigrate.Variable);
    }

    [Test]
    public void Every_container_runs_hardened_in_a_user_namespace_of_its_own()
    {
        var m = Render(exports: Exports(rabbit: true));
        var pods = Pods(m).ToList();
        pods.Should()
            .HaveCount(
                1 + 1 + 3 + 3,
                "database, rabbitmq, migrate, drain, resume and three services"
            );

        foreach (var pod in pods)
        {
            pod.Spec.HostUsers.Should().BeFalse();
            pod.Spec.AutomountServiceAccountToken.Should().BeFalse();
            var container = pod.Spec.Containers.Single();
            var security = container.SecurityContext;
            security.AllowPrivilegeEscalation.Should().BeFalse(container.Name);
            security.Capabilities.Drop.Should().Equal(["ALL"], container.Name);
            security.SeccompProfile.Type.Should().Be("RuntimeDefault");
            pod.Metadata.Annotations[
                    $"container.seccomp.security.alpha.kubernetes.io/{container.Name}"
                ]
                .Should()
                .Be("runtime/default", "podman kube play reads the annotation, not the field");
            if (container.Resources is { } resources)
                MiB(resources.Requests["memory"])
                    .Should()
                    .Be(MiB(resources.Limits["memory"]), container.Name);
            var trax = container.Name is not ("database" or "rabbitmq");
            security.ReadOnlyRootFilesystem.Should().Be(trax ? true : null, container.Name);
            if (trax)
            {
                container
                    .VolumeMounts.Should()
                    .ContainSingle(v =>
                        string.Equals(v.MountPath, "/tmp", StringComparison.Ordinal)
                    );
                pod.Spec.Volumes.Should()
                    .ContainSingle(v =>
                        string.Equals(
                            v.Name,
                            KubernetesTarget.ScratchVolume,
                            StringComparison.Ordinal
                        )
                        && v.EmptyDir != null
                    );
            }
            security
                .RunAsNonRoot.Should()
                .Be(container.Name is not ("database" or "rabbitmq"), container.Name);
        }
        MiB(
                Deployments(m)["worker"].Spec.Template.Spec.Containers.Single().Resources.Limits[
                    "memory"
                ]
            )
            .Should()
            .Be(2048);
    }

    private static long MiB(ResourceQuantity quantity) => quantity.ToInt64() / (1024 * 1024);

    [Test]
    public void Each_service_gets_the_hardening_compose_gives_it()
    {
        var file = File_();
        var compose = ComposeTarget
            .Render(file, Exports(rabbit: true))[0]
            .Content.ReplaceLineEndings("\n");
        var m = Render(file, Exports(rabbit: true));
        var containers = Pods(m).Select(p => p.Spec.Containers.Single()).ToList();

        foreach (var container in containers)
        {
            var block = Block(compose, container.Name);
            var composeAdds = block
                .Split('\n')
                .SkipWhile(l => !string.Equals(l, "    cap_add:", StringComparison.Ordinal))
                .Skip(1)
                .TakeWhile(l => l.StartsWith("      - ", StringComparison.Ordinal))
                .Select(l => l["      - ".Length..])
                .ToList();
            (container.SecurityContext.Capabilities.Add ?? [])
                .Should()
                .Equal(composeAdds, container.Name);
            var composeMemory = MemLimit.Match(block);
            if (composeMemory.Success)
                MiB(container.Resources.Limits["memory"])
                    .ToString(System.Globalization.CultureInfo.InvariantCulture)
                    .Should()
                    .Be(composeMemory.Groups["mib"].Value, container.Name);
            else
                container.Resources.Should().BeNull(container.Name);
            block.Should().Contain("      - ALL\n", "compose drops every capability too");
        }
        containers
            .Select(c => c.Name)
            .Should()
            .BeEquivalentTo([
                "database",
                "rabbitmq",
                "migrate",
                "drain",
                "hub",
                "worker",
                "reports",
                "resume",
            ]);
    }

    private static string Block(string compose, string service)
    {
        var start = compose.IndexOf($"\n  {service}:\n", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"the compose stack has a {service} service");
        var end = compose.IndexOf("\n\n", start + 1, StringComparison.Ordinal);
        return compose[start..(end < 0 ? compose.Length : end)];
    }

    [Test]
    public void No_secret_value_is_written_and_every_setting_is()
    {
        const string sentinel = "sk_live_never_in_a_manifest";
        System.Environment.SetEnvironmentVariable("Stripe__ApiKey", sentinel);
        System.Environment.SetEnvironmentVariable("POSTGRES_PASSWORD", sentinel);
        try
        {
            var m = Render(exports: Exports(rabbit: true));
            var yaml = string.Join('\n', KubernetesTarget.Yaml(m).Select(f => f.Content));

            yaml.Should().NotContain(sentinel);
            yaml.Should().NotContain("kind: Secret");
            yaml.Should().Contain("https://issuer.example.com");
            foreach (var deployment in Deployments(m).Values)
            {
                var env = deployment.Spec.Template.Spec.Containers.Single().Env;
                foreach (
                    var secret in new[] { "Stripe__ApiKey", "PGPASSWORD", "RabbitMq__Password" }
                )
                {
                    var variable = env.Single(e =>
                        string.Equals(e.Name, secret, StringComparison.Ordinal)
                    );
                    variable.Value.Should().BeNull(secret);
                    variable.ValueFrom.SecretKeyRef.Name.Should().Be("orders-staging");
                }
                env.Single(e => string.Equals(e.Name, "Auth__Authority", StringComparison.Ordinal))
                    .Value.Should()
                    .Be("https://issuer.example.com");
            }
            m.SecretName.Should().Be("orders-staging");
            m.SecretKeys.Should().Equal("POSTGRES_PASSWORD", "RABBITMQ_PASSWORD", "Stripe__ApiKey");
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("Stripe__ApiKey", value: null);
            System.Environment.SetEnvironmentVariable("POSTGRES_PASSWORD", value: null);
        }
    }

    [Test]
    public void An_existing_database_is_read_from_its_secret_and_none_runs_in_the_cluster()
    {
        var m = Render(File_(DeployEnvironment.Production));

        m.Backing.Should().BeEmpty();
        m.DatabaseSecret.NotNull().Name.Should().Be("orders-db");
        m.SecretKeys.Should().Equal("Stripe__ApiKey");
        var env = Deployments(m)["hub"].Spec.Template.Spec.Containers.Single().Env;
        var connection = env.Single(e =>
            string.Equals(e.Name, "ConnectionStrings__Trax", StringComparison.Ordinal)
        );
        connection.Value.Should().BeNull();
        connection.ValueFrom.SecretKeyRef.Name.Should().Be("orders-db");
        connection.ValueFrom.SecretKeyRef.Key.Should().Be("connection");
        env.Should().NotContain(e => e.Name == "PGPASSWORD");
    }

    [Test]
    public void Postgres_runs_in_the_cluster_under_try()
    {
        var set = Render().Backing.OfType<V1StatefulSet>().Single();

        set.Metadata.Name.Should().Be("database");
        var container = set.Spec.Template.Spec.Containers.Single();
        container.Image.Should().Be("postgres:17");
        container
            .SecurityContext.Capabilities.Add.Should()
            .Equal(ServiceHardening.Database(limits: null).AddCapabilities);
        set.Spec.VolumeClaimTemplates.Single().Metadata.Name.Should().Be("data");
    }

    [Test]
    public void Postgres_in_the_cluster_is_refused_under_production()
    {
        var file = File_(DeployEnvironment.Production);
        file.Environments["staging"].Database = new DeployEnvironmentDatabase { InCluster = true };

        var act = () => Render(file);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'staging'*production profile does not run its database in the cluster*");
    }

    [Test]
    public void RabbitMQ_in_the_cluster_is_refused_under_production()
    {
        var act = () => Render(File_(DeployEnvironment.Production), Exports(rabbit: true));

        act.Should().Throw<InvalidOperationException>().WithMessage("*RabbitMQ*production*");
    }

    [Test]
    public void A_hub_at_two_copies_is_refused()
    {
        var file = File_();
        file.Environments["staging"].Counts["hub"] = 2;

        var act = () => Render(file);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'hub'*api and scheduler*count 2*");
    }

    [Test]
    public void A_hub_is_reached_through_a_Service_and_probed_on_its_health_endpoint()
    {
        var m = Render();

        var service = m.Services.OfType<V1Service>().Single();
        service.Metadata.Name.Should().Be("hub");
        service.Spec.Type.Should().Be("ClusterIP");
        service.Spec.Ports.Single().Port.Should().Be(8080);
        var hub = Deployments(m)["hub"].Spec.Template.Spec.Containers.Single();
        hub.ReadinessProbe.HttpGet.Path.Should().Be("/trax/health");
        hub.ReadinessProbe.HttpGet.Port.ToString().Should().Be("8080");
        hub.LivenessProbe.HttpGet.Path.Should().Be("/trax/health");
        Deployments(m)["worker"]
            .Spec.Template.Spec.Containers.Single()
            .ReadinessProbe.Should()
            .BeNull("a worker reports no health endpoint");
    }

    [Test]
    public void Every_object_carries_the_app_labels()
    {
        foreach (var o in Render(exports: Exports(rabbit: true)).All)
        {
            o.Metadata.Labels[KubernetesTarget.AppLabel].Should().Be("orders");
            o.Metadata.Labels[KubernetesTarget.ManagedByLabel].Should().Be("trax");
            if (o is not V1Namespace)
            {
                o.Metadata.Labels[KubernetesTarget.PartOfLabel].Should().Be("orders");
                o.Metadata.Labels.Should().ContainKey(KubernetesTarget.NameLabel);
                o.Metadata.NamespaceProperty.Should().Be("orders-staging");
            }
        }
    }

    [Test]
    public void Images_come_from_the_registry_when_the_environment_names_one()
    {
        var local = Deployments(Render())["hub"].Spec.Template.Spec.Containers.Single();
        local.Image.Should().Be($"orders-hub:{Tag}");
        local.ImagePullPolicy.Should().Be("IfNotPresent");

        var file = File_();
        file.Environments["staging"].Registry = "ghcr.io/acme";
        file.Environments["staging"].Namespace = "shop";
        var m = Render(file);
        Deployments(m)["worker"]
            .Spec.Template.Spec.Containers.Single()
            .Image.Should()
            .Be($"ghcr.io/acme/orders-worker:{Tag}");
        m.NamespaceName.Should().Be("shop");
    }

    [Test]
    public void An_image_is_pushed_to_the_registry_split_into_host_and_repository()
    {
        var psi = DotnetContainerPublisher.StartInfo(
            "/src/Orders/Orders.csproj",
            "ghcr.io/acme",
            "orders-hub",
            Tag
        );

        psi.ArgumentList.Should()
            .Contain([
                "-t:PublishContainer",
                "-p:ContainerRegistry=ghcr.io",
                "-p:ContainerRepository=acme/orders-hub",
                $"-p:ContainerImageTags={Tag}",
            ]);
        DotnetContainerPublisher
            .StartInfo("/src/Orders/Orders.csproj", registry: null, "orders-hub", Tag)
            .ArgumentList.Should()
            .NotContain(a => a.StartsWith("-p:ContainerRegistry", StringComparison.Ordinal));
    }

    [TestCase("orders")]
    [TestCase("an-application-name-long-enough-to-crowd-out-the-tag")]
    public void A_migrate_Job_name_is_a_valid_object_name_that_keeps_the_time(string app)
    {
        var name = KubernetesTarget.MigrateJobName(
            new DeployFile { App = app },
            "20261117-120000-ABC_def"
        );

        name.Length.Should().BeLessThanOrEqualTo(63);
        name.Should().MatchRegex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$");
        name.Should().Contain("20261117-120000");
    }

    // ---- trax.deploy.json ------------------------------------------------------------------------------

    [Test]
    public async Task A_version_1_file_reads_with_aws_environments_and_writes_version_2()
    {
        var path = Path.Combine(_dir, DeployFile.FileName);
        await File.WriteAllTextAsync(
            path,
            """
            { "version": 1, "app": "orders",
              "services": { "hub": { "project": "Orders/Orders.csproj" } },
              "environments": { "production": { "region": "us-west-2", "domain": "orders.example.com" } } }
            """
        );

        var file = await DeployFile.ReadAsync(path);

        file.Environments["production"].Target.Should().Be(DeployEnvironment.Aws);
        file.Environment("production").Region.Should().Be("us-west-2");
        file.Write().Should().Contain("\"version\": 2").And.Contain("\"target\": \"aws\"");
    }

    [Test]
    public async Task A_version_2_file_with_every_target_reads_back_what_it_wrote()
    {
        var file = File_();
        file.Environments["production"] = AwsStackTemplateTests.File_().Environments["production"];
        file.Environments["local"] = new DeployEnvironment
        {
            Target = DeployEnvironment.Compose,
            Engine = DeployEnvironment.Docker,
        };
        file.Environments["staging"].Registry = "ghcr.io/acme";
        file.Settings.Add("Storefront__Origin");
        file.Environments["staging"].Settings["Storefront__Origin"] = "https://shop.example.com";
        var path = Path.Combine(_dir, DeployFile.FileName);
        await File.WriteAllTextAsync(path, file.Write());

        var read = await DeployFile.ReadAsync(path);

        read.Should().BeEquivalentTo(file);
        read.Version.Should().Be(2);
        foreach (var name in read.Environments.Keys)
            read.Environment(name).Should().NotBeNull();
    }

    [Test]
    public void A_version_2_environment_without_a_target_is_refused()
    {
        var file = File_();
        file.Environments["staging"].Target = null;

        var act = () => file.Environment("staging");

        act.Should().Throw<InvalidOperationException>().WithMessage("*'staging'*names no target*");
    }

    [TestCase("region", "*region is not a field*")]
    [TestCase("engine", "*engine is not a field*")]
    [TestCase("auth", "*auth is an aws field*")]
    [TestCase("no database", "*names no database*")]
    [TestCase("both", "*secret and inCluster both*")]
    [TestCase("namespace", "*namespace 'Shop_1'*")]
    [TestCase("registry", "*registry 'https://ghcr.io/acme'*")]
    public void A_kubernetes_environment_that_cannot_deploy_is_refused(string what, string message)
    {
        var file = File_();
        var env = file.Environments["staging"];
        switch (what)
        {
            case "region":
                env.Region = "us-west-2";
                break;
            case "engine":
                env.Engine = DeployEnvironment.Docker;
                break;
            case "auth":
                env.Auth = new DeployAuth();
                break;
            case "no database":
                env.Database = null;
                break;
            case "both":
                env.Database = new DeployEnvironmentDatabase
                {
                    InCluster = true,
                    Secret = "db",
                    Key = "k",
                };
                break;
            case "namespace":
                env.Namespace = "Shop_1";
                break;
            case "registry":
                env.Registry = "https://ghcr.io/acme";
                break;
        }

        var act = () => file.Environment("staging");

        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Test]
    public void A_kubernetes_environment_needs_no_region_or_domain() =>
        File_().Environment("staging").Region.Should().BeNull();

    [Test]
    public void A_docker_compose_stack_has_no_userns_mode_and_says_so()
    {
        var file = File_();
        var compose = ComposeTarget.Render(file, Exports(), DeployEnvironment.Docker)[0].Content;
        var podman = ComposeTarget.Render(file, Exports())[0].Content;

        compose.Should().NotContain("userns_mode:");
        compose
            .Split('\n')[1]
            .Should()
            .StartWith("# For docker compose")
            .And.Contain("userns_mode");
        podman.Should().Contain("    userns_mode: nomap");
    }

    [Test]
    public async Task Deploy_with_a_compose_environment_writes_its_engine_stack()
    {
        var file = File_();
        file.Environments["local"] = new DeployEnvironment
        {
            Target = DeployEnvironment.Compose,
            Engine = DeployEnvironment.Docker,
        };
        var path = await WriteAsync(file);

        var exit = await DeployCommand.DispatchAsync(
            path,
            target: null,
            "local",
            outDir: null,
            build: false,
            new Exporter(Exports(), _dir),
            TextWriter.Null,
            TextWriter.Null,
            default
        );

        exit.Should().Be(0);
        (await File.ReadAllTextAsync(Path.Combine(_dir, ComposeTarget.ComposeFile)))
            .Should()
            .NotContain("userns_mode:");
    }

    [TestCase("aws", "staging")]
    [TestCase("compose", "staging")]
    [TestCase("kubernetes", null)]
    public async Task A_target_that_does_not_match_or_has_no_environment_is_refused(
        string target,
        string? environment
    )
    {
        var path = await WriteAsync(File_());
        var exporter = new Exporter(Exports(), _dir);
        using var error = new StringWriter();

        (
            await DeployCommand.DispatchAsync(
                path,
                target,
                environment,
                outDir: null,
                build: false,
                exporter,
                TextWriter.Null,
                error,
                default
            )
        )
            .Should()
            .Be(1);
        exporter.Asked.Should().BeEmpty("nothing is asked of a host before the target is settled");
        error
            .ToString()
            .Should()
            .Match(
                environment is null
                    ? "*name it with --env*"
                    : $"*--target {target}*'staging'*kubernetes*"
            );
    }

    [Test]
    public async Task Secret_set_on_a_kubernetes_environment_points_at_kubectl()
    {
        var path = await WriteAsync(File_());
        using var error = new StringWriter();

        (
            await DeployAwsCommands.SecretSetAsync(
                path,
                "staging",
                "Stripe__ApiKey",
                () => Task.FromResult("x"),
                TextWriter.Null,
                error,
                default
            )
        )
            .Should()
            .Be(1);
        error
            .ToString()
            .Should()
            .Contain("kubectl create secret generic orders-staging")
            .And.Contain("--from-env-file=")
            .And.NotContain("--from-literal");
    }

    [Test]
    public async Task Init_for_kubernetes_writes_a_staging_environment_on_try_with_Postgres_in_the_cluster()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "App"));
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "App/App.csproj"),
            """<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup><PackageReference Include="Trax.Api" /></ItemGroup></Project>"""
        );
        var dir = Path.Combine(_dir, "App");

        (
            await DeployCommand.InitAsync(
                dir,
                "orders",
                force: false,
                target: "kubernetes",
                region: null,
                domain: null,
                TextWriter.Null,
                TextWriter.Null
            )
        )
            .Should()
            .Be(0);

        var file = await DeployFile.ReadAsync(Path.Combine(dir, DeployFile.FileName));
        var staging = file.Environment("staging");
        staging.Target.Should().Be(DeployEnvironment.Kubernetes);
        staging.Profile.Should().Be(DeployEnvironment.Try);
        staging.Database.NotNull().InCluster.Should().BeTrue();
    }

    [Test]
    public async Task Plan_writes_the_manifests_without_a_cluster()
    {
        var path = await WriteAsync(File_());
        DeployKubernetesCommands.Client = () =>
            throw new InvalidOperationException("plan reached for a cluster");
        try
        {
            (
                await DeployAwsCommands.PlanAsync(
                    path,
                    "staging",
                    new Exporter(Exports(), _dir),
                    TextWriter.Null,
                    TextWriter.Null,
                    CancellationToken.None
                )
            )
                .Should()
                .Be(0);
        }
        finally
        {
            DeployKubernetesCommands.Client = () =>
                new KubernetesDeployClient(DeployKubernetesCommands.Clock);
        }

        var dir = Path.Combine(_dir, ".trax", "deploy", "kubernetes", "orders-staging");
        var files = Directory
            .GetFiles(dir, "*.yaml")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();
        files[0].Should().Be("00-namespace-orders-staging.yaml");
        files.Should().Contain("08-deployment-hub.yaml");
        (await File.ReadAllTextAsync(Path.Combine(dir, "08-deployment-hub.yaml")))
            .Should()
            .Contain("kind: Deployment")
            .And.Contain("type: RollingUpdate")
            .And.Contain("maxUnavailable: 0")
            .And.Contain("hostUsers: false")
            .And.Contain("secretKeyRef:");
    }

    private async Task<string> WriteAsync(DeployFile file)
    {
        var path = Path.Combine(_dir, DeployFile.FileName);
        await File.WriteAllTextAsync(path, file.Write()).ConfigureAwait(false);
        return path;
    }

    internal sealed class Exporter(
        IReadOnlyDictionary<string, DeployManifest> byService,
        string dir
    ) : IHostExporter
    {
        public List<string> Asked { get; } = [];

        public Task<DeployManifest> ExportAsync(
            string projectPath,
            IReadOnlyDictionary<string, string> environment
        )
        {
            Asked.Add(projectPath);
            var project = Path.GetRelativePath(dir, projectPath);
            return Task.FromResult(
                project.StartsWith("Orders.Worker", StringComparison.Ordinal) ? byService["worker"]
                : project.StartsWith("Orders.Reports", StringComparison.Ordinal)
                    ? byService["reports"]
                : byService["hub"]
            );
        }
    }

    [GeneratedRegex(@"mem_limit: (?<mib>\d+)m", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MemLimit { get; }
}
