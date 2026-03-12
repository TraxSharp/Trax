using FluentAssertions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Tests for <see cref="JobSubmitterRoutingConfiguration"/> — the internal registry
/// that maps train names to specific job submitter types at dispatch time.
/// </summary>
[TestFixture]
public class JobSubmitterRoutingConfigurationTests
{
    #region Builder ForTrain routing

    [Test]
    public void GetSubmitterType_WithBuilderRoute_ReturnsConfiguredType()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.AddRoute("Ns.IMyTrain", typeof(HttpJobSubmitter));

        var result = config.GetSubmitterType("Ns.IMyTrain");

        result.Should().Be(typeof(HttpJobSubmitter));
    }

    [Test]
    public void GetSubmitterType_WithNoRoute_ReturnsNull()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.AddRoute("Ns.IMyTrain", typeof(HttpJobSubmitter));

        var result = config.GetSubmitterType("Ns.ISomeOtherTrain");

        result.Should().BeNull();
    }

    [Test]
    public void GetSubmitterType_EmptyConfig_ReturnsNull()
    {
        var config = new JobSubmitterRoutingConfiguration();

        var result = config.GetSubmitterType("Ns.IMyTrain");

        result.Should().BeNull();
    }

    #endregion

    #region [TraxRemote] attribute routing

    [Test]
    public void GetSubmitterType_WithAttributeRemoteTrain_ReturnsDefaultSubmitter()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.AddAttributeRemoteTrain("Ns.IRemoteTrain");
        config.SetAttributeDefaultSubmitter(typeof(HttpJobSubmitter));

        var result = config.GetSubmitterType("Ns.IRemoteTrain");

        result.Should().Be(typeof(HttpJobSubmitter));
    }

    [Test]
    public void GetSubmitterType_WithAttributeRemoteTrain_NoDefaultSubmitter_ReturnsNull()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.AddAttributeRemoteTrain("Ns.IRemoteTrain");

        var result = config.GetSubmitterType("Ns.IRemoteTrain");

        result.Should().BeNull();
    }

    [Test]
    public void GetSubmitterType_WithDefaultSubmitter_NonRemoteTrain_ReturnsNull()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.SetAttributeDefaultSubmitter(typeof(HttpJobSubmitter));

        var result = config.GetSubmitterType("Ns.ILocalTrain");

        result.Should().BeNull();
    }

    #endregion

    #region Precedence: builder ForTrain > [TraxRemote] attribute

    [Test]
    public void GetSubmitterType_BuilderRouteOverridesAttribute()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.AddAttributeRemoteTrain("Ns.IMyTrain");
        config.SetAttributeDefaultSubmitter(typeof(HttpJobSubmitter));
        config.AddRoute("Ns.IMyTrain", typeof(InMemoryJobSubmitter));

        var result = config.GetSubmitterType("Ns.IMyTrain");

        result.Should().Be(typeof(InMemoryJobSubmitter));
    }

    #endregion

    #region HasRoutes

    [Test]
    public void HasRoutes_EmptyConfig_ReturnsFalse()
    {
        var config = new JobSubmitterRoutingConfiguration();

        config.HasRoutes.Should().BeFalse();
    }

    [Test]
    public void HasRoutes_WithBuilderRoute_ReturnsTrue()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.AddRoute("Ns.IMyTrain", typeof(HttpJobSubmitter));

        config.HasRoutes.Should().BeTrue();
    }

    [Test]
    public void HasRoutes_WithAttributeRemoteTrainAndDefaultSubmitter_ReturnsTrue()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.AddAttributeRemoteTrain("Ns.IRemoteTrain");
        config.SetAttributeDefaultSubmitter(typeof(HttpJobSubmitter));

        config.HasRoutes.Should().BeTrue();
    }

    [Test]
    public void HasRoutes_WithAttributeRemoteTrainOnly_ReturnsFalse()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.AddAttributeRemoteTrain("Ns.IRemoteTrain");

        config.HasRoutes.Should().BeFalse();
    }

    [Test]
    public void HasRoutes_WithDefaultSubmitterOnly_ReturnsFalse()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.SetAttributeDefaultSubmitter(typeof(HttpJobSubmitter));

        config.HasRoutes.Should().BeFalse();
    }

    #endregion

    #region Multiple routes

    [Test]
    public void GetSubmitterType_MultipleRoutes_EachResolvesCorrectly()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.AddRoute("Ns.ITrainA", typeof(HttpJobSubmitter));
        config.AddRoute("Ns.ITrainB", typeof(InMemoryJobSubmitter));

        config.GetSubmitterType("Ns.ITrainA").Should().Be(typeof(HttpJobSubmitter));
        config.GetSubmitterType("Ns.ITrainB").Should().Be(typeof(InMemoryJobSubmitter));
    }

    [Test]
    public void AddRoute_SameTrainTwice_LastWins()
    {
        var config = new JobSubmitterRoutingConfiguration();
        config.AddRoute("Ns.IMyTrain", typeof(HttpJobSubmitter));
        config.AddRoute("Ns.IMyTrain", typeof(InMemoryJobSubmitter));

        config.GetSubmitterType("Ns.IMyTrain").Should().Be(typeof(InMemoryJobSubmitter));
    }

    #endregion
}
