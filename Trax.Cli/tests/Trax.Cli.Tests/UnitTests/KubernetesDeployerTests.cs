using System.Runtime.CompilerServices;
using AwesomeAssertions;
using k8s;
using k8s.Models;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax deploy --env</c> on a kubernetes environment checks the namespace is this app's and its Secret holds every
/// key before it applies anything, runs the migrate Job before any Deployment, and stops without touching a
/// Deployment when the Job fails. Destroy keeps the in-cluster database's volume unless asked.
/// </summary>
public class KubernetesDeployerTests
{
    private const string Tag = "20261117-120000-abc";

    private static (KubernetesDeployer Deployer, FakeCluster Cluster, FakeImages Images) Deployer()
    {
        var cluster = new FakeCluster();
        var images = new FakeImages();
        return (new KubernetesDeployer(cluster, images, _ => { }), cluster, images);
    }

    private static Task Deploy(KubernetesDeployer deployer, DeployFile? file = null) =>
        deployer.DeployAsync(
            file ?? DeployKubernetesTests.File_(),
            "/src/trax.deploy.json",
            "staging",
            DeployKubernetesTests.Exports(),
            Tag,
            CancellationToken.None
        );

    [Test]
    public async Task A_deploy_checks_the_secret_then_migrates_then_rolls_the_services()
    {
        var (deployer, cluster, images) = Deployer();
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];

        await Deploy(deployer);

        cluster
            .Calls.Should()
            .Equal(
                "read-namespace orders-staging",
                "secret orders-staging",
                "create-namespace orders-staging",
                "list",
                "apply NetworkPolicy/orders-deny",
                "apply NetworkPolicy/orders-within",
                "apply Service/database",
                "apply StatefulSet/database",
                "wait StatefulSet/database",
                "create-job orders-migrate-20261117-120000-abc",
                "wait-job orders-migrate-20261117-120000-abc",
                "read-deployment hub",
                "read-deployment reports",
                "read-deployment worker",
                "apply Service/hub",
                "apply Deployment/hub",
                "apply Deployment/reports",
                "apply Deployment/worker",
                "wait Deployment/hub",
                "wait Deployment/reports",
                "wait Deployment/worker"
            );
        images.Published.Should().Equal("orders-hub", "orders-reports", "orders-worker");
    }

    [Test]
    public async Task A_missing_secret_key_is_refused_before_anything_is_applied()
    {
        var (deployer, cluster, images) = Deployer();
        cluster.SecretKeys = ["POSTGRES_PASSWORD"];

        var act = () => Deploy(deployer);

        var refusal = (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        refusal
            .Should()
            .Match(
                "*has no Stripe__ApiKey*Nothing was applied*kubectl create secret generic orders-staging*"
                    + "--from-env-file=*"
            )
            .And.NotContain("--from-literal", "a value on the command line lands in shell history");
        cluster.Calls.Should().NotContain(c => c.StartsWith("apply") || c.StartsWith("create-job"));
        images.Published.Should().BeEmpty();
    }

    [Test]
    public async Task A_missing_namespace_without_its_secret_is_refused_before_it_is_created()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = null;

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*no Secret orders-staging*Nothing was applied*kubectl create namespace orders-staging*"
        );
        cluster.Calls.Should().Equal("read-namespace orders-staging", "secret orders-staging");
    }

    [Test]
    public async Task A_missing_secret_is_refused_naming_every_key()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = null;

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*no Secret orders-staging*POSTGRES_PASSWORD, Stripe__ApiKey*"
        );
    }

    [Test]
    public async Task An_existing_database_secret_must_hold_its_key()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = ["Stripe__ApiKey"];

        var act = () => Deploy(deployer, DeployKubernetesTests.File_(DeployEnvironment.Production));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*Secret orders-db*has no connection*"
        );
        cluster.Calls.Should().NotContain(c => c.StartsWith("apply"));
    }

    [Test]
    public async Task A_failed_migration_moves_no_service()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.Job = new KubernetesJobResult(
            Succeeded: false,
            "BackoffLimitExceeded",
            "[migrate-x] relation exists"
        );

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*migration failed (BackoffLimitExceeded)*no service*relation exists*"
        );
        cluster.Calls.Should().NotContain(c => c.StartsWith("apply Deployment"));
        cluster.Calls.Should().NotContain(c => c.StartsWith("wait Deployment"));
    }

    [Test]
    public async Task A_namespace_of_another_app_is_refused()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.Namespace = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KubernetesTarget.AppLabel] = "billing",
        };

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*orders-staging belongs to app 'billing'*"
        );
        cluster.Calls.Should().Equal("read-namespace orders-staging");
    }

    [Test]
    public async Task An_object_of_another_app_in_the_namespace_is_refused()
    {
        var (deployer, cluster, images) = Deployer();
        cluster.Namespace = new Dictionary<string, string>(StringComparer.Ordinal);
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.Existing.Add(new KubernetesObjectState("Deployment", "ledger", "billing"));

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*Deployment ledger*belongs to app 'billing'*"
        );
        cluster.Calls.Should().NotContain(c => c.StartsWith("apply"));
        images.Published.Should().BeEmpty();
    }

    [Test]
    public async Task An_object_trax_did_not_create_is_not_taken_over()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.Existing.Add(new KubernetesObjectState("Deployment", "hub", App: null));

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*Deployment hub*no trax/app label*"
        );
    }

    [Test]
    public async Task A_namespace_made_with_kubectl_is_used()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.Namespace = new Dictionary<string, string>(StringComparer.Ordinal);
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.Existing.Add(new KubernetesObjectState("Deployment", "hub", "orders"));

        await Deploy(deployer);

        cluster.Calls.Should().NotContain(c => c.StartsWith("create-namespace"));
        cluster.Calls.Should().Contain("wait Deployment/hub");
    }

    [Test]
    public async Task A_database_that_does_not_roll_out_stops_the_deploy_before_the_migration()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.FailRollout = "StatefulSet/database";

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "StatefulSet database did not roll out*"
        );
        cluster
            .Calls.Should()
            .NotContain(c => c.StartsWith("create-job", StringComparison.Ordinal));
        cluster
            .Calls.Should()
            .NotContain(c => c.StartsWith("apply Deployment", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_service_that_does_not_roll_out_fails_the_deploy()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.FailRollout = "Deployment/hub";

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Deployment hub did not roll out*"
        );
        cluster.Calls.Should().NotContain("wait Deployment/reports");
    }

    internal static V1Deployment Running(string name, string image) =>
        new()
        {
            ApiVersion = "apps/v1",
            Kind = "Deployment",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = "orders-staging",
                Uid = "uid-" + name,
                ResourceVersion = "41",
                Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["trax/app"] = "orders",
                },
            },
            Spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Template = new V1PodTemplateSpec
                {
                    Spec = new V1PodSpec
                    {
                        Containers = [new V1Container { Name = name, Image = image }],
                    },
                },
            },
            Status = new V1DeploymentStatus { Replicas = 1 },
        };

    [Test]
    public async Task A_failed_rollout_puts_every_service_back_on_the_version_it_ran_before()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.Deployments["hub"] = Running("hub", "orders-hub:old");
        cluster.Deployments["worker"] = Running("worker", "orders-worker:old");
        cluster.FailRollout = "Deployment/worker";

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Deployment worker did not roll out*Every service was rolled back to the version it ran before this "
                + "deploy; the migration stays."
        );
        var rollback = cluster
            .Calls.SkipWhile(c =>
                !string.Equals(c, "wait Deployment/worker", StringComparison.Ordinal)
            )
            .Skip(1)
            .ToList();
        rollback
            .Should()
            .Equal([
                "apply Deployment/hub",
                "delete Deployment/reports",
                "apply Deployment/worker",
                "wait Deployment/hub",
                "wait Deployment/worker",
                "create-job orders-resume-20261117-120000-abc",
                "wait-job orders-resume-20261117-120000-abc",
            ]);
        var restoredWorker = cluster
            .Applied.OfType<V1Deployment>()
            .Last(d => string.Equals(d.Metadata.Name, "worker", StringComparison.Ordinal));
        restoredWorker
            .Spec.Template.Spec.Containers.Single()
            .Image.Should()
            .Be("orders-worker:old");
    }

    [Test]
    public async Task A_failed_first_rollout_is_left_as_it_is()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.FailRollout = "Deployment/hub";

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Deployment hub did not roll out*Nothing was rolled back: no service ran before this deploy.*"
        );
        cluster.Calls.Should().NotContain(c => c.StartsWith("delete", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_rollback_that_fails_says_the_services_are_between_versions()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.Deployments["hub"] = Running("hub", "orders-hub:old");
        cluster.FailEveryRollout = "Deployment/hub";

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Deployment hub did not roll out*Rolling back failed too, so the services are in between versions:*"
        );
    }

    [Test]
    public async Task A_cancelled_deploy_is_not_rolled_back()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.Deployments["hub"] = Running("hub", "orders-hub:old");
        using var cancel = new CancellationTokenSource();
        cluster.OnWait = async name =>
        {
            if (string.Equals(name, "Deployment/hub", StringComparison.Ordinal))
                await cancel.CancelAsync().ConfigureAwait(false);
        };
        cluster.FailRollout = "Deployment/hub";

        var act = () =>
            deployer.DeployAsync(
                DeployKubernetesTests.File_(),
                "/src/trax.deploy.json",
                "staging",
                DeployKubernetesTests.Exports(),
                Tag,
                cancel.Token
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        cluster
            .Calls.Count(c => string.Equals(c, "apply Deployment/hub", StringComparison.Ordinal))
            .Should()
            .Be(1, "a cancelled deploy stops where it is");
    }

    [Test]
    public void A_restored_deployment_carries_its_spec_and_none_of_the_server_set_fields()
    {
        var restored = KubernetesDeployer.Restored(
            "orders-staging",
            Running("hub", "orders-hub:old")
        );

        restored.Metadata.Uid.Should().BeNull();
        restored.Metadata.ResourceVersion.Should().BeNull();
        restored.Status.Should().BeNull();
        restored.Metadata.Labels["trax/app"].Should().Be("orders");
        restored.Spec.Template.Spec.Containers.Single().Image.Should().Be("orders-hub:old");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Destroy_keeps_the_database_volume_unless_asked(bool deleteData)
    {
        var (deployer, cluster, _) = Deployer();
        cluster.Namespace = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KubernetesTarget.AppLabel] = "orders",
        };
        cluster.Existing.AddRange([
            new KubernetesObjectState("Service", "hub", "orders"),
            new KubernetesObjectState("Deployment", "hub", "orders"),
            new KubernetesObjectState("StatefulSet", "database", "orders"),
            new KubernetesObjectState("PersistentVolumeClaim", "data-database-0", "orders"),
            new KubernetesObjectState("Deployment", "ledger", App: null),
        ]);

        await deployer.DestroyAsync(
            DeployKubernetesTests.File_(),
            "staging",
            deleteData,
            CancellationToken.None
        );

        var deleted = cluster
            .Calls.Where(c => c.StartsWith("delete", StringComparison.Ordinal))
            .ToList();
        deleted
            .Should()
            .StartWith([
                "delete Deployment/hub",
                "delete StatefulSet/database",
                "delete Service/hub",
            ]);
        deleted.Should().NotContain("delete Deployment/ledger");
        deleted.Contains("delete PersistentVolumeClaim/data-database-0").Should().Be(deleteData);
    }

    internal sealed class FakeImages : IContainerImagePublisher
    {
        public List<string> Published { get; } = [];

        public List<string?> LocalRuntimes { get; } = [];

        public Task PublishAsync(
            string projectPath,
            string? registry,
            string repository,
            string tag,
            string? localRuntime = null
        )
        {
            Published.Add(repository);
            LocalRuntimes.Add(localRuntime);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeCluster : IKubernetesDeployClient
    {
        public List<string> Calls { get; } = [];

        /// <summary>The namespace's labels, or null when it does not exist.</summary>
        public Dictionary<string, string>? Namespace { get; set; }

        /// <summary>The keys of every Secret in the namespace, or null for none.</summary>
        public List<string>? SecretKeys { get; set; }

        public List<KubernetesObjectState> Existing { get; } = [];

        /// <summary>Called with kind/name at the start of every rollout wait.</summary>
        public Func<string, Task>? OnWait { get; set; }

        private readonly Dictionary<string, int> _waits = new(StringComparer.Ordinal);

        public KubernetesJobResult Job { get; set; } = new(Succeeded: true, Reason: null, "");

        /// <summary>The result of each Job whose name holds the key, over <see cref="Job"/>: "-drain-", "-resume-".</summary>
        public Dictionary<string, KubernetesJobResult> JobResults { get; } =
            new(StringComparer.Ordinal);

        /// <summary>Every Job created, in order.</summary>
        public List<V1Job> Jobs { get; } = [];

        /// <summary>How long each Job was waited for, by name.</summary>
        public Dictionary<string, TimeSpan> JobTimeouts { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The kind/name whose rollout times out, as the real client reports it. Only its first wait fails, so a
        /// rollback of it rolls out.
        /// </summary>
        public string? FailRollout { get; set; }

        /// <summary>The kind/name whose rollout fails every time, a rollback's included.</summary>
        public string? FailEveryRollout { get; set; }

        /// <summary>The Deployments the cluster holds before the deploy, by name.</summary>
        public Dictionary<string, V1Deployment> Deployments { get; } = new(StringComparer.Ordinal);

        /// <summary>Every body applied, in order.</summary>
        public List<IKubernetesObject<V1ObjectMeta>> Applied { get; } = [];

        public List<KubernetesWorkloadState> Workloads { get; } = [];

        public List<string> LogLines { get; } = [];

        public string Context => "kind-test";

        public string Server { get; set; } = "https://kind-test:6443";

        public Task<IReadOnlyDictionary<string, string>?> NamespaceLabelsAsync(
            string name,
            CancellationToken ct
        )
        {
            Calls.Add($"read-namespace {name}");
            return Task.FromResult<IReadOnlyDictionary<string, string>?>(Namespace);
        }

        public Task CreateNamespaceAsync(V1Namespace body, CancellationToken ct)
        {
            Calls.Add($"create-namespace {body.Metadata.Name}");
            Namespace = new Dictionary<string, string>(
                body.Metadata.Labels,
                StringComparer.Ordinal
            );
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<string>?> SecretKeysAsync(
            string ns,
            string name,
            CancellationToken ct
        )
        {
            Calls.Add($"secret {name}");
            return Task.FromResult<IReadOnlyCollection<string>?>(SecretKeys);
        }

        /// <summary>The labels of each Secret by name, for the ones whose labels are read.</summary>
        public Dictionary<string, Dictionary<string, string>> SecretLabels { get; } =
            new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, string>?> SecretLabelsAsync(
            string ns,
            string name,
            CancellationToken ct
        )
        {
            Calls.Add($"secret-labels {name}");
            return Task.FromResult<IReadOnlyDictionary<string, string>?>(
                SecretLabels.GetValueOrDefault(name)
            );
        }

        /// <summary>The value of each tailnet proxy Secret by name, as an earlier deploy wrote it.</summary>
        public Dictionary<string, string> ProxySecrets { get; } = new(StringComparer.Ordinal);

        public Task<string?> ProxySecretAsync(string ns, string name, CancellationToken ct)
        {
            Calls.Add($"read-proxy-secret {name}");
            return Task.FromResult(ProxySecrets.GetValueOrDefault(name));
        }

        public Task<IReadOnlyList<KubernetesObjectState>> ListAsync(string ns, CancellationToken ct)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<KubernetesObjectState>>(Existing);
        }

        /// <summary>The cluster-wide objects the cluster holds, by kind/name.</summary>
        public Dictionary<string, KubernetesObjectState> ClusterObjects { get; } =
            new(StringComparer.Ordinal);

        public Task<KubernetesObjectState?> ClusterObjectAsync(
            string kind,
            string name,
            CancellationToken ct
        ) => Task.FromResult(ClusterObjects.GetValueOrDefault($"{kind}/{name}"));

        public Task ApplyAsync(IKubernetesObject<V1ObjectMeta> body, CancellationToken ct)
        {
            Calls.Add($"apply {body.Kind}/{body.Metadata.Name}");
            Applied.Add(body);
            return Task.CompletedTask;
        }

        public Task<V1Deployment?> ReadDeploymentAsync(string ns, string name, CancellationToken ct)
        {
            Calls.Add($"read-deployment {name}");
            return Task.FromResult(Deployments.GetValueOrDefault(name));
        }

        /// <summary>Called with each Job before it is created; throws to stand for the API refusing it.</summary>
        public Action<V1Job>? OnCreateJob { get; set; }

        public Task CreateJobAsync(V1Job body, CancellationToken ct)
        {
            OnCreateJob?.Invoke(body);
            Calls.Add($"create-job {body.Metadata.Name}");
            Jobs.Add(body);
            return Task.CompletedTask;
        }

        public Task<KubernetesJobResult> WaitForJobAsync(
            string ns,
            string name,
            TimeSpan timeout,
            CancellationToken ct
        )
        {
            Calls.Add($"wait-job {name}");
            JobTimeouts[name] = timeout;
            var result = JobResults
                .Where(r => name.Contains(r.Key, StringComparison.Ordinal))
                .Select(r => r.Value)
                .FirstOrDefault();
            return Task.FromResult(result ?? Job);
        }

        public async Task WaitForRolloutAsync(
            string ns,
            string kind,
            string name,
            TimeSpan timeout,
            CancellationToken ct
        )
        {
            var target = $"{kind}/{name}";
            Calls.Add($"wait {target}");
            if (OnWait is not null)
                await OnWait(target).ConfigureAwait(false);
            _waits[target] = _waits.GetValueOrDefault(target) + 1;
            var failOnce =
                string.Equals(FailRollout, target, StringComparison.Ordinal) && _waits[target] == 1;
            if (failOnce || string.Equals(FailEveryRollout, target, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{kind} {name} did not roll out within 10 minutes"
                );
        }

        public Task<IReadOnlyList<KubernetesWorkloadState>> WorkloadsAsync(
            string ns,
            string app,
            CancellationToken ct
        )
        {
            Calls.Add($"workloads {app}");
            return Task.FromResult<IReadOnlyList<KubernetesWorkloadState>>(Workloads);
        }

        public async IAsyncEnumerable<string> LogsAsync(
            string ns,
            string app,
            string service,
            bool follow,
            int sinceMinutes,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            Calls.Add($"logs {service}");
            foreach (var line in LogLines)
            {
                await Task.Yield();
                yield return line;
            }
        }

        public Task DeleteAsync(string ns, KubernetesObjectState target, CancellationToken ct)
        {
            Calls.Add($"delete {target.Kind}/{target.Name}");
            return Task.CompletedTask;
        }
    }
}
