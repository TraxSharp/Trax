using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// <c>AddLifecycleHook&lt;THook&gt;()</c> "resolves your hook's constructor dependencies from DI",
/// and its documented use is audit logs and the like. A hook created for a run in a scope should
/// get that scope's services, not the root container's.
///
/// <para>Enforces <c>docs/adr/0011-a-service-train-instance-is-one-run.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0011-a-service-train-instance-is-one-run.md")]
public class LifecycleHookScopeTests
{
    private static readonly List<IDataContext> SeenContexts = [];

    private static ServiceProvider Build(bool validateScopes)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().AddLifecycleHook<AuditHook>())
        );
        services.AddScopedTraxRoute<IAuditedTrain, AuditedTrain>();
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = validateScopes, ValidateOnBuild = false }
        );
    }

    [SetUp]
    public void Reset() => SeenContexts.Clear();

    [Test]
    public async Task A_hook_with_a_scoped_dependency_gets_the_runs_scope_not_the_root()
    {
        await using var provider = Build(validateScopes: false);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IAuditedTrain>().Run(Unit.Default);
        }

        SeenContexts.Should().HaveCount(2);
        SeenContexts[0]
            .Should()
            .NotBeSameAs(
                SeenContexts[1],
                "0011-a-service-train-instance-is-one-run.md: two runs in two scopes; a scoped IDataContext shared between them is a captive "
                    + "root-scoped DbContext used concurrently by every train in the process"
            );
    }

    [Test]
    public async Task A_hook_with_a_scoped_dependency_runs_when_the_container_validates_scopes()
    {
        // ASP.NET Core turns ValidateScopes on in Development.
        await using var provider = Build(validateScopes: true);
        using var scope = provider.CreateScope();

        var run = async () =>
            await scope.ServiceProvider.GetRequiredService<IAuditedTrain>().Run(Unit.Default);

        await run.Should()
            .NotThrowAsync(
                "0011-a-service-train-instance-is-one-run.md: the hook's dependency is registered, "
                    + "and the run has a scope to take it from"
            );
    }

    private sealed class AuditHook(IDataContext audit) : ITrainLifecycleHook
    {
        public Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            SeenContexts.Add(audit);
            return Task.CompletedTask;
        }
    }

    private class AuditedTrain : ServiceTrain<Unit, Unit>, IAuditedTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IAuditedTrain : IServiceTrain<Unit, Unit> { }
}
