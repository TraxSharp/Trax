using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.StepProvider.Logging.Extensions;
using Trax.Effect.StepProvider.Progress.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

[TestFixture]
public class SaneDefaultsTests
{
    #region Parameterless AddEffects

    [Test]
    public void AddEffects_Parameterless_RegistersEffectConfiguration()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax => trax.AddEffects());

        using var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<ITraxEffectConfiguration>();
        config.Should().NotBeNull();
        config.LogLevel.Should().Be(LogLevel.Debug);
    }

    [Test]
    public void AddEffects_Parameterless_RegistersEffectRegistry()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax => trax.AddEffects());

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IEffectRegistry>();
        registry.Should().NotBeNull();
    }

    #endregion

    #region Func<> Overload

    [Test]
    public void AddEffects_ExpressionLambda_CompilesAndRegistersConfiguration()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax => trax.AddEffects(effects => effects));

        using var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<ITraxEffectConfiguration>();
        config.Should().NotBeNull();
    }

    [Test]
    public void AddEffects_FuncWithChaining_PreservesConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.SetEffectLogLevel(LogLevel.Trace).AddJson())
        );

        using var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<ITraxEffectConfiguration>();
        config.LogLevel.Should().Be(LogLevel.Trace);
    }

    #endregion

    #region UseInMemory Returns TraxEffectBuilderWithData

    [Test]
    public void UseInMemory_ReturnsTraxEffectBuilderWithData()
    {
        var services = new ServiceCollection();

        TraxEffectBuilderWithData? capturedBuilder = null;

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                var result = effects.UseInMemory();
                capturedBuilder = result;
                return result;
            })
        );

        capturedBuilder.Should().NotBeNull();
        capturedBuilder.Should().BeOfType<TraxEffectBuilderWithData>();
    }

    #endregion

    #region Generic Type Preservation

    [Test]
    public void AddJson_PreservesTraxEffectBuilderWithData()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        TraxEffectBuilder? capturedBuilder = null;

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                var result = effects.UseInMemory().AddJson();
                capturedBuilder = result;
                return result;
            })
        );

        capturedBuilder.Should().BeOfType<TraxEffectBuilderWithData>();
    }

    [Test]
    public void SaveTrainParameters_PreservesTraxEffectBuilderWithData()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        TraxEffectBuilder? capturedBuilder = null;

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                var result = effects.UseInMemory().SaveTrainParameters();
                capturedBuilder = result;
                return result;
            })
        );

        capturedBuilder.Should().BeOfType<TraxEffectBuilderWithData>();
    }

    [Test]
    public void AddStepProgress_PreservesTraxEffectBuilderWithData()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        TraxEffectBuilder? capturedBuilder = null;

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                var result = effects.UseInMemory().AddStepProgress();
                capturedBuilder = result;
                return result;
            })
        );

        capturedBuilder.Should().BeOfType<TraxEffectBuilderWithData>();
    }

    [Test]
    public void AddStepLogger_PreservesTraxEffectBuilderWithData()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        TraxEffectBuilder? capturedBuilder = null;

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                var result = effects.UseInMemory().AddStepLogger();
                capturedBuilder = result;
                return result;
            })
        );

        capturedBuilder.Should().BeOfType<TraxEffectBuilderWithData>();
    }

    [Test]
    public void SetEffectLogLevel_PreservesTraxEffectBuilderWithData()
    {
        var services = new ServiceCollection();

        TraxEffectBuilder? capturedBuilder = null;

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                var result = effects.UseInMemory().SetEffectLogLevel(LogLevel.Warning);
                capturedBuilder = result;
                return result;
            })
        );

        capturedBuilder.Should().BeOfType<TraxEffectBuilderWithData>();
    }

    [Test]
    public void FullChain_PreservesTraxEffectBuilderWithData()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        TraxEffectBuilder? capturedBuilder = null;

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                var result = effects
                    .UseInMemory()
                    .AddJson()
                    .SaveTrainParameters()
                    .AddStepProgress()
                    .AddStepLogger()
                    .SetEffectLogLevel(LogLevel.Trace);
                capturedBuilder = result;
                return result;
            })
        );

        capturedBuilder.Should().BeOfType<TraxEffectBuilderWithData>();
    }

    #endregion

    #region AddDataContextLogging Compile-Time Safety

    [Test]
    public void AddDataContextLogging_AfterUseInMemory_Works()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // This compiles because UseInMemory returns TraxEffectBuilderWithData
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().AddDataContextLogging())
        );

        using var provider = services.BuildServiceProvider();
        var loggerProvider = provider.GetService<ILoggerProvider>();
        loggerProvider.Should().NotBeNull();
    }

    [Test]
    public void AddDataContextLogging_AfterUseInMemoryAndOtherEffects_Works()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // AddDataContextLogging works after chaining through generic-preserving methods
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects.UseInMemory().AddJson().AddDataContextLogging().SaveTrainParameters()
            )
        );

        using var provider = services.BuildServiceProvider();
        var loggerProvider = provider.GetService<ILoggerProvider>();
        loggerProvider.Should().NotBeNull();
    }

    // Note: The following scenario is enforced at COMPILE TIME and cannot be expressed
    // as a runtime test:
    //
    //   effects.AddJson().AddDataContextLogging()
    //
    // This does NOT compile because AddJson<T>() returns TraxEffectBuilder (not WithData),
    // and AddDataContextLogging() is only defined on TraxEffectBuilderWithData.
    //
    // This is the core compile-time safety guarantee of the typed builder pattern.

    #endregion

    #region TraxBuilderWithEffects Flag Exposure

    [Test]
    public void TraxBuilderWithEffects_AfterUsePostgres_HasDatabaseProviderTrue()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var connectionString =
            "Host=localhost;Port=5432;Database=trax_data_tests;Username=trax;Password=trax123";
        Trax.Effect.Configuration.TraxBuilder.TraxBuilderWithEffects? captured = null;

        services.AddTrax(trax =>
        {
            captured = trax.AddEffects(effects => effects.UsePostgres(connectionString));
        });

        captured.Should().NotBeNull();
        captured!.HasDatabaseProvider.Should().BeTrue();
    }

    [Test]
    public void TraxBuilderWithEffects_AfterUseInMemory_HasDatabaseProviderFalse()
    {
        var services = new ServiceCollection();

        Trax.Effect.Configuration.TraxBuilder.TraxBuilderWithEffects? captured = null;

        services.AddTrax(trax =>
        {
            captured = trax.AddEffects(effects => effects.UseInMemory());
        });

        captured.Should().NotBeNull();
        captured!.HasDatabaseProvider.Should().BeFalse();
    }

    [Test]
    public void TraxBuilderWithEffects_AfterUsePostgres_HasDataProviderTrue()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var connectionString =
            "Host=localhost;Port=5432;Database=trax_data_tests;Username=trax;Password=trax123";
        Trax.Effect.Configuration.TraxBuilder.TraxBuilderWithEffects? captured = null;

        services.AddTrax(trax =>
        {
            captured = trax.AddEffects(effects => effects.UsePostgres(connectionString));
        });

        captured.Should().NotBeNull();
        captured!.HasDataProvider.Should().BeTrue();
    }

    [Test]
    public void TraxBuilderWithEffects_AfterUseInMemory_HasDataProviderTrue()
    {
        var services = new ServiceCollection();

        Trax.Effect.Configuration.TraxBuilder.TraxBuilderWithEffects? captured = null;

        services.AddTrax(trax =>
        {
            captured = trax.AddEffects(effects => effects.UseInMemory());
        });

        captured.Should().NotBeNull();
        captured!.HasDataProvider.Should().BeTrue();
    }

    #endregion

    #region AddStepProgress Build-Time Validation

    [Test]
    public void AddStepProgress_WithoutDataProvider_ThrowsWithHelpfulMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax => trax.AddEffects(effects => effects.AddStepProgress()));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddStepProgress()*")
            .WithMessage("*UsePostgres*")
            .WithMessage("*UseInMemory*");
    }

    [Test]
    public void AddStepProgress_WithInMemory_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory().AddStepProgress())
            );

        act.Should().NotThrow();
    }

    [Test]
    public void AddStepProgress_BeforeDataProvider_StillPasses()
    {
        // Validation runs at Build() time, after the lambda returns,
        // so ordering within the lambda doesn't matter.
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects =>
                {
                    effects.AddStepProgress();
                    return effects.UseInMemory();
                })
            );

        act.Should().NotThrow();
    }

    [Test]
    public void AddStepProgress_ErrorMessage_ContainsCodeExample()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax => trax.AddEffects(effects => effects.AddStepProgress()));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*services.AddTrax*")
            .WithMessage("*.AddEffects*")
            .WithMessage("*.UsePostgres(connectionString)*");
    }

    #endregion
}
