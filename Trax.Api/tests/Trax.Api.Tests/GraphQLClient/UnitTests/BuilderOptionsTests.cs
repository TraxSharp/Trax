using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// Each builder method mutates the underlying GraphQLClientConfigurationBuilder. Without
/// these tests, a regression in a Builder.With*/Configure* method (forgot to assign, wrong
/// property, dropped the chain return) would only surface as silent behavior change in
/// downstream calls. Each test pulls the resolved configuration and asserts the specific
/// property the method was supposed to set.
/// </summary>
[TestFixture]
public class BuilderOptionsTests
{
    private static IGraphQLClientConfiguration BuildConfiguration(TraxGraphQLClientBuilder builder)
    {
        var sp = builder.Services.BuildServiceProvider();
        return sp.GetRequiredService<IGraphQLClientConfiguration>();
    }

    [Test]
    public void WithStrictness_SetsConfigurationStrictness()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddTraxGraphQLClient(new Uri("http://stub/graphql"))
            .WithStrictness(ResponseStrictness.WarnOnDrift);

        var config = BuildConfiguration(builder);

        config.ResponseStrictness.Should().Be(ResponseStrictness.WarnOnDrift);
    }

    [Test]
    public void ConfigureHttpClient_AssignsCustomHttpClient()
    {
        // Real regression this catches: a typo in the setter would leave the default
        // HttpClient in place and silently break tests/dev environments that need a
        // custom handler (auth, test server, etc.).
        var custom = new HttpClient();
        var services = new ServiceCollection();
        var builder = services
            .AddTraxGraphQLClient(new Uri("http://stub/graphql"))
            .ConfigureHttpClient(custom);

        var config = BuildConfiguration(builder);

        config.HttpClient.Should().BeSameAs(custom);
    }

    [Test]
    public void ConfigureHttpClient_NullClient_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddTraxGraphQLClient(new Uri("http://stub/graphql"));

        var act = () => builder.ConfigureHttpClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void DisposeHttpClient_True_FlowsToConfiguration()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddTraxGraphQLClient(new Uri("http://stub/graphql"))
            .DisposeHttpClient(true);

        var config = BuildConfiguration(builder);

        config.DisposeHttpClient.Should().BeTrue();
    }

    [Test]
    public void DisposeHttpClient_DefaultIsFalse()
    {
        var services = new ServiceCollection();
        services.AddTraxGraphQLClient(new Uri("http://stub/graphql"));

        var config = services
            .BuildServiceProvider()
            .GetRequiredService<IGraphQLClientConfiguration>();

        config
            .DisposeHttpClient.Should()
            .BeFalse("consumer owns the HttpClient lifetime by default");
    }

    [Test]
    public void ConfigureJson_ReplacesSerializerOptions()
    {
        var custom = new JsonSerializerOptions { WriteIndented = true };
        var services = new ServiceCollection();
        var builder = services
            .AddTraxGraphQLClient(new Uri("http://stub/graphql"))
            .ConfigureJson(custom);

        var config = BuildConfiguration(builder);

        config.JsonSerializerOptions.Should().BeSameAs(custom);
    }

    [Test]
    public void ConfigureJson_NullOptions_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddTraxGraphQLClient(new Uri("http://stub/graphql"));

        var act = () => builder.ConfigureJson(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Configure_EscapeHatch_MutatesUnderlyingBuilder()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddTraxGraphQLClient(new Uri("http://stub/graphql"))
            .Configure(b => b.RemoveSubscriptionsFromSchema = false);

        var config = BuildConfiguration(builder);

        config.RemoveSubscriptionsFromSchema.Should().BeFalse();
    }

    [Test]
    public void Configure_NullMutator_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddTraxGraphQLClient(new Uri("http://stub/graphql"));

        var act = () => builder.Configure(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Builder_ChainsReturnSameInstance()
    {
        // Fluent contract: every With*/Use*/Configure* must return the same builder. A
        // regression that returned a new instance would silently break long chains.
        var services = new ServiceCollection();
        var b1 = services.AddTraxGraphQLClient(new Uri("http://stub/graphql"));
        var b2 = b1.WithStrictness(ResponseStrictness.WarnOnDrift)
            .DisposeHttpClient(true)
            .Configure(_ => { });

        b2.Should().BeSameAs(b1);
    }

    [Test]
    public void AddTraxGraphQLClient_NullServices_Throws()
    {
        IServiceCollection services = null!;
        var act = () => services.AddTraxGraphQLClient(new Uri("http://stub/graphql"));
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddTraxGraphQLClient_NullUri_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddTraxGraphQLClient(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
