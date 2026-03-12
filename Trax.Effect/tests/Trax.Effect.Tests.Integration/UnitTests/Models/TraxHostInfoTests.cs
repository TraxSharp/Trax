using FluentAssertions;
using Trax.Effect.Models.Host;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

[TestFixture]
[NonParallelizable]
public class TraxHostInfoTests
{
    private readonly string[] _envVarsToClean =
    [
        "AWS_LAMBDA_FUNCTION_NAME",
        "AWS_LAMBDA_LOG_STREAM_NAME",
        "ECS_CONTAINER_METADATA_URI",
        "ECS_CONTAINER_METADATA_URI_V4",
        "KUBERNETES_SERVICE_HOST",
        "WEBSITE_SITE_NAME",
        "WEBSITE_INSTANCE_ID",
        "HOSTNAME",
    ];

    [TearDown]
    public void TearDown()
    {
        TraxHostInfo.Current = null;
        foreach (var envVar in _envVarsToClean)
            Environment.SetEnvironmentVariable(envVar, null);
    }

    #region AutoDetect Environment

    [Test]
    public void AutoDetect_LambdaEnvVar_DetectsLambda()
    {
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME", "my-function");

        var info = TraxHostInfo.AutoDetect();

        info.HostEnvironment.Should().Be("lambda");
    }

    [Test]
    public void AutoDetect_EcsEnvVarV4_DetectsEcs()
    {
        Environment.SetEnvironmentVariable(
            "ECS_CONTAINER_METADATA_URI_V4",
            "http://169.254.170.2/v4/abc"
        );

        var info = TraxHostInfo.AutoDetect();

        info.HostEnvironment.Should().Be("ecs");
    }

    [Test]
    public void AutoDetect_EcsEnvVar_DetectsEcs()
    {
        Environment.SetEnvironmentVariable(
            "ECS_CONTAINER_METADATA_URI",
            "http://169.254.170.2/v3/abc"
        );

        var info = TraxHostInfo.AutoDetect();

        info.HostEnvironment.Should().Be("ecs");
    }

    [Test]
    public void AutoDetect_KubernetesEnvVar_DetectsKubernetes()
    {
        Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", "10.0.0.1");

        var info = TraxHostInfo.AutoDetect();

        info.HostEnvironment.Should().Be("kubernetes");
    }

    [Test]
    public void AutoDetect_AzureEnvVar_DetectsAzureAppService()
    {
        Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", "my-app");

        var info = TraxHostInfo.AutoDetect();

        info.HostEnvironment.Should().Be("azure-app-service");
    }

    [Test]
    public void AutoDetect_NoEnvVars_DefaultsToServer()
    {
        var info = TraxHostInfo.AutoDetect();

        info.HostEnvironment.Should().Be("server");
    }

    [Test]
    public void AutoDetect_MultipleEnvVars_LambdaWinsOverEcs()
    {
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME", "my-function");
        Environment.SetEnvironmentVariable(
            "ECS_CONTAINER_METADATA_URI_V4",
            "http://169.254.170.2/v4/abc"
        );

        var info = TraxHostInfo.AutoDetect();

        info.HostEnvironment.Should().Be("lambda");
    }

    #endregion

    #region AutoDetect InstanceId

    [Test]
    public void AutoDetect_Lambda_UsesLogStreamName()
    {
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME", "my-function");
        Environment.SetEnvironmentVariable(
            "AWS_LAMBDA_LOG_STREAM_NAME",
            "2024/03/12/[$LATEST]abc123"
        );

        var info = TraxHostInfo.AutoDetect();

        info.HostInstanceId.Should().Be("2024/03/12/[$LATEST]abc123");
    }

    [Test]
    public void AutoDetect_NoSpecificEnvVars_UsesMachineNameAndPid()
    {
        var info = TraxHostInfo.AutoDetect();

        info.HostInstanceId.Should().Be($"{Environment.MachineName}-{Environment.ProcessId}");
    }

    #endregion

    #region AutoDetect HostName

    [Test]
    public void AutoDetect_SetsHostNameToMachineName()
    {
        var info = TraxHostInfo.AutoDetect();

        info.HostName.Should().Be(Environment.MachineName);
    }

    #endregion

    #region Labels

    [Test]
    public void AutoDetect_Labels_DefaultsToEmpty()
    {
        var info = TraxHostInfo.AutoDetect();

        info.Labels.Should().BeEmpty();
    }

    #endregion
}
