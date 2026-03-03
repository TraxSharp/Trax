using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Api.GraphQL.Trains.Greet;

public interface IGreetTrain : IServiceTrain<GreetInput, Unit>;
