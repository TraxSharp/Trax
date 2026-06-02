using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GraphQLClient.InventoryServer.Trains;

public record GetProductInput(string Sku);

public record GetProductOutput(string Sku, string Name, int QuantityOnHand);

public interface IGetProductTrain : IServiceTrain<GetProductInput, GetProductOutput>;

// Exposed at discover.inventory.getProduct. The "inventory" namespace is this server's
// schema grouping — it has nothing to do with the DI key a client picks to reach this
// server (see the Gateway sample).
[TraxAllowAnonymous]
[TraxQuery(Namespace = "inventory", Description = "Looks up a product by SKU.")]
public class GetProductTrain : ServiceTrain<GetProductInput, GetProductOutput>, IGetProductTrain
{
    protected override Task<Either<Exception, GetProductOutput>> Junctions() =>
        Chain<GetProductJunction>().Resolve();
}

internal sealed class GetProductJunction : Junction<GetProductInput, GetProductOutput>
{
    private static readonly IReadOnlyDictionary<string, GetProductOutput> Catalog = new Dictionary<
        string,
        GetProductOutput
    >
    {
        ["SKU-1"] = new("SKU-1", "Mechanical Keyboard", 42),
        ["SKU-2"] = new("SKU-2", "Wireless Mouse", 17),
    };

    public override Task<GetProductOutput> Run(GetProductInput input) =>
        Task.FromResult(
            Catalog.TryGetValue(input.Sku, out var product)
                ? product
                : new GetProductOutput(input.Sku, "Unknown", 0)
        );
}
