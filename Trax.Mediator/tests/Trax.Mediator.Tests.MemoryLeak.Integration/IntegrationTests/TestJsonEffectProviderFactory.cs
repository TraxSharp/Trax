using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Json.Services.JsonEffect;
using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.EffectRunner;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

public class TestJsonEffectProviderFactory(
    JsonSerializerOptions options,
    ITraxEffectConfiguration configuration
) : IEffectProviderFactory
{
    private readonly JsonSerializerOptions _options = options;

    public Trax.Effect.Services.EffectProvider.IEffectProvider Create()
    {
        return new JsonEffectProvider(NullLogger<JsonEffectProvider>.Instance, configuration);
    }
}
