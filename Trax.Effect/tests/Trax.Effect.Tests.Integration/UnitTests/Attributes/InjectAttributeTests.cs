using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Attributes;

[TestFixture]
public class InjectAttributeTests
{
    [Test]
    public void InjectAttribute_CanBeAppliedToProperty()
    {
        // Arrange & Act
        var property = typeof(TestInjectable).GetProperty(nameof(TestInjectable.InjectedService));
        var attribute = property?.GetCustomAttribute<InjectAttribute>();

        // Assert
        attribute.Should().NotBeNull();
    }

    [Test]
    public void InjectAttribute_TargetsPropertyOnly()
    {
        // Arrange & Act
        var attributeUsage = typeof(InjectAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        attributeUsage.Should().NotBeNull();
        attributeUsage!.ValidOn.Should().Be(AttributeTargets.Property);
    }

    [Test]
    public void InjectProperties_SetsMarkedProperties()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService>(new TestService());
        using var provider = services.BuildServiceProvider();
        var instance = new TestInjectable();

        // Act
        provider.InjectProperties(instance);

        // Assert
        instance.InjectedService.Should().NotBeNull();
        instance.InjectedService.Should().BeOfType<TestService>();
    }

    [Test]
    public void InjectProperties_SkipsNonMarkedProperties()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService>(new TestService());
        using var provider = services.BuildServiceProvider();
        var instance = new TestInjectable();

        // Act
        provider.InjectProperties(instance);

        // Assert
        instance.NonInjectedService.Should().BeNull();
    }

    #region Test helpers

    private interface ITestService { }

    private class TestService : ITestService { }

    private class TestInjectable
    {
        [Inject]
        public ITestService? InjectedService { get; set; }

        public ITestService? NonInjectedService { get; set; }
    }

    #endregion
}
