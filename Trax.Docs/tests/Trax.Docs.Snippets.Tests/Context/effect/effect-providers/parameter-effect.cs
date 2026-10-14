// What the compiled examples on effect/effect-providers/parameter-effect.md take as given.
global using System.Text.Json;
global using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.ServiceTrain;

public record GetEntitiesQuery(string Kind);

public interface IDeltaImportAllTrain : IServiceTrain<string, int>;

public interface IPatchCustomerTrain : IServiceTrain<string, int>;
