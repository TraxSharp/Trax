using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

public class FindOrderJunction : Junction<FindOrderInput, OrderOutput>
{
    public override Task<OrderOutput> Run(FindOrderInput input)
    {
        var order = new OrderOutput
        {
            Title = string.Create(CultureInfo.InvariantCulture, $"Order {input.Id}"),
        };
        var customer = new CustomerOutput { Name = "Ada", Orders = [order] };
        return Task.FromResult(order with { Customer = customer });
    }
}
