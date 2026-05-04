using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Configuration;
using Trax.Mediator.Extensions;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class TraxBuilderWithMediatorGettersTests
{
    [Test]
    public void TraxBuilderWithMediator_ExposesRootEffectsAndProviderFlags()
    {
        TraxBuilderWithMediator? captured = null;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );

        // Build the same chain explicitly so we can capture the state-marker type.
        var capture = new ServiceCollection();
        capture.AddLogging();
        capture.AddTrax(trax =>
        {
            var withEffects = trax.AddEffects(e => e.UseInMemory());
            captured = withEffects.AddMediator(assemblies: [typeof(AssemblyMarker).Assembly]);
        });

        captured.Should().NotBeNull();
        captured!.ServiceCollection.Should().BeSameAs(capture);
        captured.HasDataProvider.Should().BeTrue();
        captured.HasDatabaseProvider.Should().BeFalse();
        captured.Root.Should().NotBeNull();
    }
}
