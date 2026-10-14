// What the examples under sdk-reference/mediator-api take as given: the usings and the trains
// they dispatch. The host (services, connectionString, assembly) comes from _shared.cs.
global using System.Text.Json;
global using Microsoft.AspNetCore.Mvc;
global using Trax.Core.Functional;
global using Trax.Core.Junction;
global using Trax.Effect.Attributes;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Data.Services.EnqueueContext;
global using Trax.Effect.Extensions;
global using Trax.Effect.Models.Metadata;
global using Trax.Effect.Services.ServiceTrain;
global using Trax.Mediator.Configuration;
global using Trax.Mediator.Exceptions;
global using Trax.Mediator.Extensions;
global using Trax.Mediator.Services.Principal;
global using Trax.Mediator.Services.TrainBus;
global using Trax.Mediator.Services.TrainDiscovery;
global using Trax.Mediator.Services.TrainExecution;
global using Trax.Mediator.Services.TrustedExecution;

public record OrderInput
{
    public decimal Total { get; init; }
}

public record OrderResult
{
    public string PaymentId { get; init; } = "";
}

public record PaymentInput
{
    public decimal Amount { get; init; }
}

public record PaymentResult
{
    public string Id { get; init; } = "";
}

public interface IProcessOrderTrain : IServiceTrain<OrderInput, OrderResult>;

public class ChargeOrderJunction : Junction<OrderInput, OrderResult>
{
    public override Task<OrderResult> Run(OrderInput input) =>
        Task.FromResult(new OrderResult { PaymentId = "pay-1" });
}
