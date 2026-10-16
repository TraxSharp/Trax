---
title: Handle errors in trains
description: "How to raise, translate and react to failures in a train: throwing from a junction, the OnFailed hook, and telling a failure from a cancellation."
type: how-to
area: trains
nav_order: 1
source:
  - Trax.Core/src/Trax.Core/Junction/Junction.cs
  - Trax.Effect/src/Trax.Effect/Services/ServiceTrain/ServiceTrain.cs
redirect_from:
  - /docs/cross-cutting/common-patterns
---

# Handle errors in trains

A junction fails by throwing. The chain catches the exception, skips the junctions after it and
hands the exception back to the caller with its type and message unchanged. Why it works that way
is in [The Railway](/docs/core#the-railway); this page is what to write.

That is what code in the same process sees. A GraphQL caller sees less, so nothing internal leaks:
only a plain `TrainException`'s message reaches it, as `TRAX_TRAIN_ERROR`. A subclass of
`TrainException` becomes "The train failed.", and any other exception, an
`InvalidOperationException` included, becomes HotChocolate's masked "Unexpected Execution Error".
Throw a plain `TrainException` for a message the caller should read. See
[Error codes](/docs/sdk-reference/graphql-api/error-codes).

## Fail a junction

Throw the exception you want the caller to see. To turn a dependency's exception into one of your
own, catch it in the junction that calls the dependency:

```csharp compile=errors
using Trax.Core.Junction;

public class ChargeCardJunction(IPaymentGateway gateway) : Junction<PaymentRequest, PaymentResult>
{
    public override async Task<PaymentResult> Run(PaymentRequest input)
    {
        try
        {
            return await gateway.ChargeAsync(input, CancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new PaymentException("Payment gateway timed out", ex);
        }
    }
}
```

Do not catch an exception only to return a default value: the next junction then runs on input
that is not real.

## React to a failure in the train

On a `ServiceTrain`, override `OnFailed`. It runs once, after the run's failure is recorded, with
the exception and the run's metadata:

```csharp compile=errors
using Trax.Core.Functional;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

public interface IProcessOrderTrain : IServiceTrain<ProcessOrderRequest, ProcessOrderResult>;

public class ProcessOrderTrain
    : ServiceTrain<ProcessOrderRequest, ProcessOrderResult>,
        IProcessOrderTrain
{
    protected override Task<Either<Exception, ProcessOrderResult>> Junctions() =>
        Chain<ValidateOrderJunction>()
            .Chain<ChargeCardJunction>()
            .Chain<FulfillOrderJunction>()
            .Resolve();

    protected override Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        if (exception is PaymentException payment)
            Logger?.LogWarning(
                "Payment failed for order {OrderId}: {Error}",
                metadata.GetInput<ProcessOrderRequest>()?.OrderId,
                payment.Message
            );

        return Task.CompletedTask;
    }
}
```

A cancellation that someone asked for calls `OnCancelled` instead, and the run is recorded as
`Cancelled`. An `OperationCanceledException` nobody asked for, such as an `HttpClient` timeout, is a
failure and calls `OnFailed`. The hooks are listed on
[ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train); for a hook that runs for
every train, see [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook).

## Handle the failure in the caller

`Run`, and `RunAsync` on the train bus, throw the junction's exception. Catch the types you can do
something about, and keep a cancellation apart from a failure:

```csharp compile=errors
using Trax.Mediator.Services.TrainBus;

public class CheckoutService(ITrainBus trainBus)
{
    public async Task<string> Checkout(ProcessOrderRequest request, CancellationToken ct)
    {
        try
        {
            var result = await trainBus.RunAsync<ProcessOrderResult>(request, ct);
            return $"Order {result.OrderId} placed";
        }
        catch (PaymentException ex)
        {
            return $"Payment declined: {ex.Message}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return "Cancelled";
        }
    }
}
```

On a plain `Train`, `RunEither` returns the exception as `Left` instead of throwing it, a
cancellation included, so check for `OperationCanceledException` before treating a `Left` as a
business failure:

```csharp compile=errors
using Trax.Core.Functional;
using Trax.Core.Train;

public class QuoteTrain : Train<QuoteRequest, decimal>
{
    protected override Task<Either<Exception, decimal>> Junctions() =>
        Chain<PriceQuoteJunction>().Resolve();
}

public static class Quotes
{
    public static async Task<string> Describe(QuoteTrain train, QuoteRequest request)
    {
        var result = await train.RunEither(request);

        return result.Match(
            Left: exception =>
                exception is OperationCanceledException ? "Cancelled" : $"Failed: {exception.Message}",
            Right: price => $"Quoted {price:C}"
        );
    }
}
```

## Record what kind of failure it was

To record whether a failure is transient, a conflict or permanent, so the dashboard and your own
code can act on it without matching exception messages, register an
[IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier).
[Failure classification](/docs/effect/failure-classification) explains what it is asked and when.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Resolve](/docs/sdk-reference/train-methods/resolve) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [RunAsync](/docs/sdk-reference/mediator-api/i-train-bus) | [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train)
