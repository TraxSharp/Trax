// What the examples in the trax-test-your-app skill take as given: a train in the app under test.
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace MyApp.Trains.PlaceOrder
{
    public record PlaceOrderInput;

    public interface IPlaceOrderTrain : IServiceTrain<PlaceOrderInput, Unit>;
}
