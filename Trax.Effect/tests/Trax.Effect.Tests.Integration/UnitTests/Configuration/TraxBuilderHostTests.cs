using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Host;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

[TestFixture]
[NonParallelizable]
public class TraxBuilderHostTests
{
    [TearDown]
    public void TearDown()
    {
        TraxHostInfo.Current = null;
    }

    #region SetHostEnvironment

    [Test]
    public void SetHostEnvironment_OverridesAutoDetectedValue()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax => trax.SetHostEnvironment("custom-env"));

        TraxHostInfo.Current.Should().NotBeNull();
        TraxHostInfo.Current!.HostEnvironment.Should().Be("custom-env");
    }

    [Test]
    public void SetHostEnvironment_Null_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrax(trax => trax.SetHostEnvironment(null!));

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region SetHostInstanceId

    [Test]
    public void SetHostInstanceId_OverridesAutoDetectedValue()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax => trax.SetHostInstanceId("instance-42"));

        TraxHostInfo.Current.Should().NotBeNull();
        TraxHostInfo.Current!.HostInstanceId.Should().Be("instance-42");
    }

    #endregion

    #region AddHostLabel

    [Test]
    public void AddHostLabel_SingleLabel_AppearsInCurrent()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax => trax.AddHostLabel("region", "us-east-1"));

        TraxHostInfo.Current!.Labels.Should().ContainKey("region");
        TraxHostInfo.Current.Labels["region"].Should().Be("us-east-1");
    }

    [Test]
    public void AddHostLabel_MultipleCalls_MergesAll()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax =>
            trax.AddHostLabel("region", "us-east-1").AddHostLabel("service", "content-shield")
        );

        TraxHostInfo.Current!.Labels.Should().HaveCount(2);
        TraxHostInfo.Current.Labels["region"].Should().Be("us-east-1");
        TraxHostInfo.Current.Labels["service"].Should().Be("content-shield");
    }

    [Test]
    public void AddHostLabel_DuplicateKey_LastWins()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax =>
            trax.AddHostLabel("region", "us-east-1").AddHostLabel("region", "eu-west-1")
        );

        TraxHostInfo.Current!.Labels["region"].Should().Be("eu-west-1");
    }

    [Test]
    public void AddHostLabels_Dictionary_MergesWithExisting()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax =>
            trax.AddHostLabel("existing", "value")
                .AddHostLabels(
                    new Dictionary<string, string>
                    {
                        ["region"] = "us-east-1",
                        ["team"] = "platform",
                    }
                )
        );

        TraxHostInfo.Current!.Labels.Should().HaveCount(3);
        TraxHostInfo.Current.Labels["existing"].Should().Be("value");
        TraxHostInfo.Current.Labels["region"].Should().Be("us-east-1");
        TraxHostInfo.Current.Labels["team"].Should().Be("platform");
    }

    #endregion

    #region Default Behavior

    [Test]
    public void Build_NoHostConfig_AutoDetectsHostInfo()
    {
        var services = new ServiceCollection();

        services.AddTrax(_ => { });

        TraxHostInfo.Current.Should().NotBeNull();
        TraxHostInfo.Current!.HostName.Should().NotBeNullOrEmpty();
        TraxHostInfo.Current.HostEnvironment.Should().NotBeNullOrEmpty();
        TraxHostInfo.Current.HostInstanceId.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void Build_SetsTraxHostInfoCurrent()
    {
        var services = new ServiceCollection();

        services.AddTrax(_ => { });

        TraxHostInfo.Current.Should().NotBeNull();
    }

    #endregion
}
