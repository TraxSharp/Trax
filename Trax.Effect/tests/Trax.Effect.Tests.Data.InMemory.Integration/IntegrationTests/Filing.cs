using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

// A consumer's namespace that starts with "System", which is not a framework namespace.
namespace Systematic.Tests;

public sealed record Filing(string Id, [property: TraxSensitive] string TaxId);
