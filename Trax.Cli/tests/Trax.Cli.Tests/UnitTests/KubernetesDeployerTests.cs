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
                "create-namespace orders-staging",
                "secret orders-staging",
                "list",
                "apply Service/database",
                "apply StatefulSet/database",
                "wait StatefulSet/database",
                "create-job orders-migrate-20261117-120000-abc",
                "wait-job orders-migrate-20261117-120000-abc",
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

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*has no Stripe__ApiKey*Nothing was applied*kubectl create secret generic orders-staging*"
        );
        cluster.Calls.Should().NotContain(c => c.StartsWith("apply") || c.StartsWith("create-job"));
        images.Published.Should().BeEmpty();
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
            false,
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
        cluster.Namespace = new Dictionary<string, string>
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
        cluster.Namespace = new Dictionary<string, string>();
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
        cluster.Existing.Add(new KubernetesObjectState("Deployment", "hub", null));

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*Deployment hub*no trax/app label*"
        );
    }

    [Test]
    public async Task A_namespace_made_with_kubectl_is_used()
    {
        var (deployer, cluster, _) = Deployer();
        cluster.Namespace = new Dictionary<string, string>();
        cluster.SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"];
        cluster.Existing.Add(new KubernetesObjectState("Deployment", "hub", "orders"));

        await Deploy(deployer);

        cluster.Calls.Should().NotContain(c => c.StartsWith("create-namespace"));
        cluster.Calls.Should().Contain("wait Deployment/hub");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Destroy_keeps_the_database_volume_unless_asked(bool deleteData)
    {
        var (deployer, cluster, _) = Deployer();
        cluster.Namespace = new Dictionary<string, string>
        {
            [KubernetesTarget.AppLabel] = "orders",
        };
        cluster.Existing.AddRange([
            new KubernetesObjectState("Service", "hub", "orders"),
            new KubernetesObjectState("Deployment", "hub", "orders"),
            new KubernetesObjectState("StatefulSet", "database", "orders"),
            new KubernetesObjectState("PersistentVolumeClaim", "data-database-0", "orders"),
            new KubernetesObjectState("Deployment", "ledger", null),
        ]);

        await deployer.DestroyAsync(
            DeployKubernetesTests.File_(),
            "staging",
            deleteData,
            CancellationToken.None
        );

        var deleted = cluster.Calls.Where(c => c.StartsWith("delete")).ToList();
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

        public void Publish(string projectPath, string? registry, string repository, string tag) =>
            Published.Add(repository);
    }

    internal sealed class FakeCluster : IKubernetesDeployClient
    {
        public List<string> Calls { get; } = [];

        /// <summary>The namespace's labels, or null when it does not exist.</summary>
        public Dictionary<string, string>? Namespace { get; set; }

        /// <summary>The keys of every Secret in the namespace, or null for none.</summary>
        public List<string>? SecretKeys { get; set; }

        public List<KubernetesObjectState> Existing { get; } = [];

        public KubernetesJobResult Job { get; set; } = new(true, null, "");

        public string Context => "kind-test";

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
            Namespace = new Dictionary<string, string>(body.Metadata.Labels);
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

        public Task<IReadOnlyList<KubernetesObjectState>> ListAsync(string ns, CancellationToken ct)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<KubernetesObjectState>>(Existing);
        }

        public Task ApplyAsync(IKubernetesObject<V1ObjectMeta> body, CancellationToken ct)
        {
            Calls.Add($"apply {body.Kind}/{body.Metadata.Name}");
            return Task.CompletedTask;
        }

        public Task CreateJobAsync(V1Job body, CancellationToken ct)
        {
            Calls.Add($"create-job {body.Metadata.Name}");
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
            return Task.FromResult(Job);
        }

        public Task WaitForRolloutAsync(
            string ns,
            string kind,
            string name,
            TimeSpan timeout,
            CancellationToken ct
        )
        {
            Calls.Add($"wait {kind}/{name}");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KubernetesWorkloadState>> WorkloadsAsync(
            string ns,
            string app,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<KubernetesWorkloadState>>([]);

        public async IAsyncEnumerable<string> LogsAsync(
            string ns,
            string app,
            string service,
            bool follow,
            int sinceMinutes,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task DeleteAsync(string ns, KubernetesObjectState target, CancellationToken ct)
        {
            Calls.Add($"delete {target.Kind}/{target.Name}");
            return Task.CompletedTask;
        }
    }
}
