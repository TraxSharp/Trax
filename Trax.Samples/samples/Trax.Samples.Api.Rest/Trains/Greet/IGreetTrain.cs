using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Api.Rest.Trains.Greet;

public interface IGreetTrain : IServiceTrain<GreetInput, Unit>;
