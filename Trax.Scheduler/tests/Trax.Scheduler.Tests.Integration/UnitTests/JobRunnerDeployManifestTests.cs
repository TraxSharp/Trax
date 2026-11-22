using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Data.Extensions;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// A host that maps a runner endpoint reports the <c>job-runner</c> role in its deploy manifest. One that only
/// registers the execution pipeline, as a worker or an API-only host does, does not.
/// </summary>
[TestFixture]
[NonParallelizable]
public class JobRunnerDeployManifestTests
{
    private string _path = null!;

    [SetUp]
    public void NewPath() =>
        _path = Path.Combine(Path.GetTempPath(), $"trax-deploy-{Guid.NewGuid():N}", "deploy.json");

    [TearDown]
    public void RemovePath()
    {
        if (Directory.Exists(Path.GetDirectoryName(_path)))
            Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);
    }

    [Test]
    public async Task A_host_that_maps_the_runner_endpoint_reports_the_job_runner_role()
    {
        await using var app = Build(app => app.UseTraxJobRunner());

        await app.RunTraxAsync(["--trax-export-deploy", _path]);

        (await Manifest()).Roles.Should().Equal("job-runner");
    }

    [Test]
    public async Task A_host_that_maps_the_run_endpoint_reports_the_job_runner_role()
    {
        await using var app = Build(app => app.UseTraxRunEndpoint());

        await app.RunTraxAsync(["--trax-export-deploy", _path]);

        (await Manifest()).Roles.Should().Equal("job-runner");
    }

    [Test]
    public async Task A_host_that_registers_the_runner_without_an_endpoint_reports_no_role()
    {
        await using var app = Build(_ => { });

        await app.RunTraxAsync(["--trax-export-deploy", _path]);

        (await Manifest()).Roles.Should().BeEmpty();
    }

    private async Task<DeployManifest> Manifest() =>
        DeployManifest.Read(await File.ReadAllTextAsync(_path));

    private static WebApplication Build(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );
        builder.WebHost.UseTestServer();
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Services.AddTraxJobRunner(runner => runner.AllowUnsignedRequests());

        var app = builder.Build();
        map(app);
        return app;
    }
}
