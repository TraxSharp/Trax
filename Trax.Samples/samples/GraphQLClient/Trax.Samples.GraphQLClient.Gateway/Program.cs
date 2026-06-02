using Trax.Api.GraphQL.Client;
using Trax.Samples.GraphQLClient.BillingServer;
using Trax.Samples.GraphQLClient.BillingServer.Trains;
using Trax.Samples.GraphQLClient.Gateway.Requests;
using Trax.Samples.GraphQLClient.InventoryServer;
using Trax.Samples.GraphQLClient.InventoryServer.Trains;

// One consumer ("server A") talking to two downstream Trax servers with different schemas,
// using two keyed clients registered in a SINGLE container. The key names the server; the
// request's Path names the schema namespace on that server. This sample starts both servers
// in-process so it runs with a single `dotnet run`.

const string inventoryUrl = "http://localhost:5310";
const string billingUrl = "http://localhost:5311";

var inventory = InventoryServerHost.Build([]);
inventory.Urls.Add(inventoryUrl);
await inventory.StartAsync();

var billing = BillingServerHost.Build([]);
billing.Urls.Add(billingUrl);
await billing.StartAsync();

using (var probe = new HttpClient())
{
    await WaitForHealthyAsync(probe, $"{inventoryUrl}/trax/health");
    await WaitForHealthyAsync(probe, $"{billingUrl}/trax/health");
}

var services = new ServiceCollection();
services.AddKeyedTraxGraphQLClient("serverB", new Uri($"{inventoryUrl}/trax/graphql"));
services.AddKeyedTraxGraphQLClient("serverC", new Uri($"{billingUrl}/trax/graphql"));

// The consumer uses its own container for the keyed clients to keep the sample
// self-contained; a real app would resolve the executors from the host container.
#pragma warning disable ASP0000
await using var provider = services.BuildServiceProvider();
#pragma warning restore ASP0000
var inventoryClient = provider.GetRequiredKeyedService<IGraphQLClientExecutor>("serverB");
var billingClient = provider.GetRequiredKeyedService<IGraphQLClientExecutor>("serverC");

var product = await inventoryClient.Run(
    new GetProductRequest { Input = new GetProductInput("SKU-1") }
);
Console.WriteLine($"serverB (inventory) -> {product.Name}, {product.QuantityOnHand} on hand");

var invoice = await billingClient.Run(
    new GetInvoiceRequest { Input = new GetInvoiceInput("INV-1") }
);
Console.WriteLine(
    $"serverC (billing)   -> invoice {invoice.InvoiceId}: {invoice.AmountCents}c ({invoice.Status})"
);

// The "serverB" client validates against the inventory schema, which has no getInvoice, so a
// billing query is rejected before any HTTP call. That is the isolation keyed clients buy.
try
{
    await inventoryClient.Run(new GetInvoiceRequest { Input = new GetInvoiceInput("INV-1") });
    Console.WriteLine("UNEXPECTED: the inventory client accepted a billing query");
}
catch (GraphQLValidationException)
{
    Console.WriteLine("serverB rejected a billing query (schema isolation holds)");
}

await inventory.StopAsync();
await billing.StopAsync();
return 0;

static async Task WaitForHealthyAsync(HttpClient client, string healthUrl)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            var response = await client.GetAsync(healthUrl);
            if (response.IsSuccessStatusCode)
                return;
        }
        catch (HttpRequestException)
        {
            // server not up yet
        }

        await Task.Delay(100);
    }

    throw new TimeoutException($"Server at {healthUrl} did not become healthy within 30s.");
}
