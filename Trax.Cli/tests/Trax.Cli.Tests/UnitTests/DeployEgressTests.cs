using System.Globalization;
using AwesomeAssertions;
using Trax.Cli.Deploy;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A service's <c>egress</c>: the domains it may reach, checked when the file is read, and the egress image every
/// target that runs containers renders beside it (<c>Trax.Docs/adr/0064</c>). Compose gets a sidecar the service
/// joins; Kubernetes an init container that applies the rules and a native sidecar, with an admission policy that
/// allows the rules their NET_ADMIN and nothing else; podman the same pod with the sidecar as an ordinary container;
/// ECS refuses it. A service that names no egress gets none of it.
/// </summary>
public partial class DeployEgressTests
{
    internal const string Image =
        "ghcr.io/traxsharp/trax-egress@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-egress-{Guid.NewGuid():N}");
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

    private async Task<DeployFile> ReadAsync(DeployFile file)
    {
        var path = Path.Combine(_dir, DeployFile.FileName);
        await File.WriteAllTextAsync(path, file.Write()).ConfigureAwait(false);
        return await DeployFile.ReadAsync(path).ConfigureAwait(false);
    }

    /// <summary>The kubernetes file with the hub held to one domain.</summary>
    internal static DeployFile Held(string profile = DeployEnvironment.Try)
    {
        var file = DeployKubernetesTests.File_(profile);
        file.EgressImage = Image;
        file.Services["hub"].Egress = ["api.anthropic.com", "registry.example.com:8443"];
        return file;
    }

    // ---- The file ---------------------------------------------------------------------------------------

    [Test]
    public async Task Domains_with_and_without_a_port_are_read_back_as_written()
    {
        var file = await ReadAsync(Held());

        file.Services["hub"]
            .Egress.Should()
            .Equal("api.anthropic.com", "registry.example.com:8443");
        file.Services["worker"].Egress.Should().BeNull();
        file.EgressImage.Should().Be(Image);
    }

    [Test]
    public async Task An_empty_list_is_kept_as_deny_everything_else()
    {
        var file = Held();
        file.Services["hub"].Egress = [];

        (await ReadAsync(file)).Services["hub"].Egress.Should().NotBeNull().And.BeEmpty();
    }

    [TestCase("*.anthropic.com")]
    [TestCase("https://api.anthropic.com")]
    [TestCase("api.anthropic.com/v1")]
    [TestCase("API.anthropic.com")]
    [TestCase("localhost")]
    [TestCase("database")]
    [TestCase("1.1.1.1")]
    [TestCase("169.254.169.254:80")]
    [TestCase("[2606:4700::1111]:443")]
    [TestCase("tcp:database:5432")]
    [TestCase("api.anthropic.com:0")]
    [TestCase("api.anthropic.com:65536")]
    [TestCase("api.anthropic.com:0443")]
    [TestCase("api.anthropic.com:")]
    [TestCase("-bad.example.com")]
    [TestCase("bad-.example.com")]
    [TestCase("a..example.com")]
    [TestCase("exa_mple.com")]
    [TestCase("example.com.")]
    [TestCase("")]
    [TestCase(" api.anthropic.com")]
    public async Task A_domain_the_image_would_refuse_is_refused_when_the_file_is_read(string entry)
    {
        var file = Held();
        file.Services["hub"].Egress = [entry];

        await FluentActions
            .Awaiting(() => ReadAsync(file))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*service 'hub' has egress '{entry}', which is not a domain*");
    }

    [Test]
    public async Task A_label_longer_than_63_or_a_name_longer_than_253_is_refused()
    {
        var file = Held();
        foreach (
            var entry in new[]
            {
                new string('a', 64) + ".com",
                string.Concat(Enumerable.Repeat("abcdefghi.", 26)) + "com",
            }
        )
        {
            file.Services["hub"].Egress = [entry];
            await FluentActions
                .Awaiting(() => ReadAsync(file))
                .Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*which is not a domain*");
        }
    }

    [Test]
    public async Task A_domain_named_twice_is_refused()
    {
        var file = Held();
        file.Services["hub"].Egress = ["api.anthropic.com", "api.anthropic.com:443"];

        await FluentActions
            .Awaiting(() => ReadAsync(file))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*names egress 'api.anthropic.com:443' twice*");
    }

    [Test]
    public async Task More_domains_than_the_limit_are_refused()
    {
        var file = Held();
        file.Services["hub"].Egress =
        [
            .. Enumerable
                .Range(0, DeployEgress.MaxEntries + 1)
                .Select(i => string.Create(CultureInfo.InvariantCulture, $"host{i}.example.com")),
        ];

        await FluentActions
            .Awaiting(() => ReadAsync(file))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*at most 64*");
    }

    [Test]
    public async Task A_service_named_like_another_services_sidecar_is_refused()
    {
        var file = Held();
        file.Services["hub-egress"] = new DeployService
        {
            Project = "Orders.Reports/Orders.Reports.csproj",
        };

        await FluentActions
            .Awaiting(() => ReadAsync(file))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*sidecar would be named 'hub-egress', which is another service*");
    }

    [TestCase("ghcr.io/traxsharp/trax-egress:1.2.3")]
    [TestCase("ghcr.io/traxsharp/trax-egress@sha256:abc")]
    [TestCase(
        "trax-egress@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    )]
    [TestCase(
        "GHCR.io/traxsharp/trax-egress@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    )]
    public async Task An_egress_image_not_named_by_digest_is_refused(string image)
    {
        var file = Held();
        file.EgressImage = image;

        await FluentActions
            .Awaiting(() => ReadAsync(file))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*egressImage '{image}' is not an image named by digest*");
    }

    [Test]
    public void A_development_build_names_no_image_and_says_how_to_name_one()
    {
        DeployEgress.BuiltIn.Should().BeNull("the tests are not built by the Release workflow");
        var file = Held();
        file.EgressImage = null;

        FluentActions
            .Invoking(() => DeployEgress.Image(file))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*development build*\"egressImage\"*@sha256:<digest>*");
    }

    [Test]
    public void The_file_checks_egress_on_every_environment_it_hands_out()
    {
        var file = Held();
        file.Services["hub"].Egress = ["*.example.com"];

        FluentActions
            .Invoking(() => file.Environment("staging"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Environment 'staging': service 'hub' has egress '*.example.com'*");
    }

    // ---- ECS --------------------------------------------------------------------------------------------

    [Test]
    public void An_aws_environment_refuses_a_service_that_names_egress_and_says_where_to_go()
    {
        var file = Held();
        file.Settings.Clear();
        file.Environments["staging"] = new DeployEnvironment
        {
            Target = DeployEnvironment.Aws,
            Profile = DeployEnvironment.Try,
            Region = "us-west-2",
        };

        FluentActions
            .Invoking(() => file.Environment("staging"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "Environment 'staging': service 'hub' names egress, which aws (ECS Fargate) cannot hold it to*"
                    + "NET_ADMIN*\"target\": \"kubernetes\"*EKS*"
            );
    }

    [Test]
    public void An_aws_environment_refuses_an_empty_egress_list_too()
    {
        var file = Held();
        file.Settings.Clear();
        file.Services["hub"].Egress = [];
        file.Environments["staging"] = new DeployEnvironment
        {
            Target = DeployEnvironment.Aws,
            Profile = DeployEnvironment.Try,
            Region = "us-west-2",
        };

        FluentActions
            .Invoking(() => file.Environment("staging"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*service 'hub' names egress*");
    }

    [Test]
    public void An_aws_environment_takes_a_file_with_no_egress()
    {
        var file = DeployKubernetesTests.File_();
        file.Settings.Clear();
        file.Environments["staging"] = new DeployEnvironment
        {
            Target = DeployEnvironment.Aws,
            Profile = DeployEnvironment.Try,
            Region = "us-west-2",
        };

        file.Environment("staging").Target.Should().Be(DeployEnvironment.Aws);
    }
}
