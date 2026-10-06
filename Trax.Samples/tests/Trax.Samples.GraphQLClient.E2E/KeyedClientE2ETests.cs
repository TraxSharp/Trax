namespace Trax.Samples.GraphQLClient.E2E;

/// <summary>
/// End-to-end against two real running Trax servers with different schemas, reached through
/// two keyed clients in one container. Each client validates against and queries its OWN
/// server; a query meant for the other server is rejected by schema validation before any
/// HTTP call. Servers run in-memory, so the suite needs no database.
/// </summary>
[TestFixture]
public class KeyedClientE2ETests
{
    private static IGraphQLClientExecutor Executor(string key) =>
        SharedKeyedClientsSetup.Clients.GetRequiredKeyedService<IGraphQLClientExecutor>(key);

    [Test]
    public async Task InventoryKey_RunsProductQuery_AgainstInventoryServer()
    {
        var product = await Executor("serverB")
            .Run(new GetProductRequest { Input = new GetProductInput("SKU-1") });

        product.Sku.Should().Be("SKU-1");
        product.Name.Should().Be("Mechanical Keyboard");
        product.QuantityOnHand.Should().Be(42);
    }

    [Test]
    public async Task BillingKey_RunsInvoiceQuery_AgainstBillingServer()
    {
        var invoice = await Executor("serverC")
            .Run(new GetInvoiceRequest { Input = new GetInvoiceInput("INV-2") });

        invoice.InvoiceId.Should().Be("INV-2");
        invoice.AmountCents.Should().Be(12_500);
        invoice.Status.Should().Be("Pending");
    }

    [Test]
    public async Task InvoiceQuery_ThroughInventoryKey_FailsSchemaValidation()
    {
        // The inventory schema has no getInvoice. The "serverB" client validates against that
        // schema and rejects the billing query before any HTTP call — the isolation guarantee.
        var act = async () =>
            await Executor("serverB")
                .Run(new GetInvoiceRequest { Input = new GetInvoiceInput("INV-1") });

        await act.Should().ThrowAsync<GraphQLValidationException>();
    }

    [Test]
    public async Task ProductQuery_ThroughBillingKey_FailsSchemaValidation()
    {
        var act = async () =>
            await Executor("serverC")
                .Run(new GetProductRequest { Input = new GetProductInput("SKU-1") });

        await act.Should().ThrowAsync<GraphQLValidationException>();
    }

    [Test]
    public async Task InventoryKey_UnknownSku_ReturnsNotFoundFallback()
    {
        var product = await Executor("serverB")
            .Run(new GetProductRequest { Input = new GetProductInput("SKU-MISSING") });

        product.Sku.Should().Be("SKU-MISSING");
        product.Name.Should().Be("Unknown");
        product.QuantityOnHand.Should().Be(0);
    }

    [Test]
    public async Task BillingKey_UnknownInvoiceId_ReturnsNotFoundFallback()
    {
        var invoice = await Executor("serverC")
            .Run(new GetInvoiceRequest { Input = new GetInvoiceInput("INV-MISSING") });

        invoice.InvoiceId.Should().Be("INV-MISSING");
        invoice.AmountCents.Should().Be(0);
        invoice.Status.Should().Be("Unknown");
    }

    [Test]
    public void BothKeys_ResolveDistinctExecutors_FromSameContainer()
    {
        Executor("serverB").Should().NotBeSameAs(Executor("serverC"));
    }
}
