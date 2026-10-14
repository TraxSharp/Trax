---
title: Make a junction cancellable
description: "How to pass a request's CancellationToken into a train, use it in a junction's async calls and loops, let a run stop from another host, and test it."
type: how-to
area: trains
nav_order: 4
source:
  - Trax.Core/src/Trax.Core/Junction/Junction.cs
  - Trax.Effect/src/Trax.Effect.JunctionProvider.Progress/Extensions/ServiceExtensions.cs
---

# Make a junction cancellable

A junction stops early only where it hands its token to something that can stop. These steps
make that happen; why the token works the way it does is in
[How cancellation works](/docs/cross-cutting/cancellation-tokens).

## 1. Pass a token to the train

Give `Run`, or `RunAsync` on the train bus, the token of whatever started the work. In ASP.NET
Core that is the request's token, which fires when the client disconnects:

```csharp compile=cancel
using Trax.Mediator.Services.TrainBus;

app.MapPost(
    "/orders",
    async (CreateOrderRequest request, ITrainBus trainBus, CancellationToken ct) =>
        await trainBus.RunAsync<OrderResult>(request, ct)
);
```

A train the scheduler runs already gets the worker's token. You pass one only where your code
starts the run.

## 2. Pass `this.CancellationToken` to every async call

The chain sets the junction's `CancellationToken` before it calls `Run`. Hand it to each call that
takes one:

```csharp compile=cancel
using Trax.Core.Junction;

public class FetchQuoteJunction(IHttpClientFactory httpFactory) : Junction<QuoteRequest, Quote>
{
    public override async Task<Quote> Run(QuoteRequest input)
    {
        var client = httpFactory.CreateClient();

        var response = await client.GetAsync(input.Url, CancellationToken);
        response.EnsureSuccessStatusCode();

        var quote = await response.Content.ReadFromJsonAsync<Quote>(CancellationToken);
        return quote ?? throw new InvalidOperationException("The quote service returned no body.");
    }
}
```

The same goes for EF Core queries and saves on your own `DbContext`, `Task.Delay`, channel writes
and stream reads:

```csharp compile=cancel
using Microsoft.EntityFrameworkCore;
using Trax.Core.Junction;

public class LoadCustomerJunction(ShopDbContext db) : Junction<CustomerId, Customer>
{
    public override async Task<Customer> Run(CustomerId input) =>
        await db.Customers.FirstOrDefaultAsync(c => c.Id == input.Value, CancellationToken)
        ?? throw new KeyNotFoundException($"Customer {input.Value} not found");
}
```

## 3. Check the token in long loops

A loop of work that takes no token, such as CPU-bound work or a call to a library without a token
overload, checks it once per iteration:

```csharp compile=cancel
using Trax.Core.Junction;

public class ResizeImagesJunction(IImageResizer resizer) : Junction<ImageBatch, ResizedImages>
{
    public override Task<ResizedImages> Run(ImageBatch input)
    {
        var resized = new List<byte[]>();

        foreach (var image in input.Images)
        {
            CancellationToken.ThrowIfCancellationRequested();
            resized.Add(resizer.Resize(image));
        }

        return Task.FromResult(new ResizedImages(resized));
    }
}
```

Throw the `OperationCanceledException` that `ThrowIfCancellationRequested` raises; do not catch it.
While the token is cancelled, the chain treats it as cancellation rather than as the junction's
failure.

## 4. Let a run stop from another host

A cancel from the dashboard or `CancelAsync`, and a scheduler timeout, reach a run on another host
only through a database flag that is read before each `EffectJunction`. To make a junction a point
where such a run can stop, derive it from `EffectJunction` instead of `Junction`:

```csharp compile=cancel
using Trax.Effect.Services.EffectJunction;

public class SendInvoiceJunction(IInvoiceSender sender) : EffectJunction<Invoice, InvoiceReceipt>
{
    public override Task<InvoiceReceipt> Run(Invoice input) =>
        sender.SendAsync(input, CancellationToken);
}
```

and register the check with `AddJunctionProgress()`:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJunctionProgress()
    )
);
```

The flag is read before the junction starts, so it does not interrupt a junction already running.
A run on the same host as the cancel is stopped at once through its token either way.

## 5. Test it

Run the train with a token you control. A pre-cancelled token runs no junction, and the junction
sees the token it was given:

```csharp fragment
[Test]
public async Task A_cancelled_token_runs_no_junction()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var junction = new CountingJunction();
    var train = new SingleJunctionTrain(junction);

    var act = () => train.Run("input", cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
    junction.ExecutionCount.Should().Be(0);
}

[Test]
public async Task The_junction_receives_the_token()
{
    using var cts = new CancellationTokenSource();
    var junction = new TokenCapturingJunction();
    var train = new SingleJunctionTrain(junction);

    await train.Run("input", cts.Token);

    junction.CapturedToken.Should().Be(cts.Token);
}
```

The helpers are a train and two junctions written for the test:

```csharp compile
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;

public class SingleJunctionTrain(Junction<string, string> junction) : Train<string, string>
{
    protected override Task<Either<Exception, string>> Junctions() => Chain(junction).Resolve();
}

public class CountingJunction : Junction<string, string>
{
    public int ExecutionCount { get; private set; }

    public override Task<string> Run(string input)
    {
        ExecutionCount++;
        return Task.FromResult(input);
    }
}

public class TokenCapturingJunction : Junction<string, string>
{
    public CancellationToken CapturedToken { get; private set; }

    public override Task<string> Run(string input)
    {
        CapturedToken = CancellationToken;
        return Task.FromResult(input);
    }
}
```

To test a cancel that arrives mid-junction, give the junction a `Task.Delay` on its token and
cancel the source with `CancelAfter`; the run throws `OperationCanceledException`.

## SDK Reference

> [Run / RunEither](/docs/sdk-reference/train-methods/run) | [RunAsync](/docs/sdk-reference/mediator-api/i-train-bus) | [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress) | [CancelAsync](/docs/sdk-reference/scheduler-api/manifest-management)
