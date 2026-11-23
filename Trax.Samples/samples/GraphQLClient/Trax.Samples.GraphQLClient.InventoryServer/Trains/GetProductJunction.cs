using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GraphQLClient.InventoryServer.Trains;

internal sealed class GetProductJunction : Junction<GetProductInput, GetProductOutput>
{
    private static readonly IReadOnlyDictionary<string, GetProductOutput> Catalog = new Dictionary<
        string,
        GetProductOutput
    >(StringComparer.Ordinal)
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
