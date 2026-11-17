using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Integration.Examples.Caching;

public class CachedPriceTests
{
    // Shown on https://traxsharp.net/docs/sdk-reference/train-methods/short-circuit
    //
    // A price lookup with a cache in front. A cache hit becomes the result, but the steps after it
    // still run. A cache miss throws, and the train carries on as if the step were not there.

    public sealed record Product(string Sku);

    public sealed record Price(decimal Amount);

    public interface IPriceCache
    {
        IDictionary<string, decimal> Prices { get; }
    }

    public sealed class PriceCache : IPriceCache
    {
        public IDictionary<string, decimal> Prices { get; } =
            new Dictionary<string, decimal>(StringComparer.Ordinal);
    }

    public interface IAudit
    {
        IList<string> Lines { get; }
    }

    public sealed class Audit : IAudit
    {
        public IList<string> Lines { get; } = [];
    }

    public class CheckCache(IPriceCache cache) : Junction<Product, Price>
    {
        public override Task<Price> Run(Product product) =>
            cache.Prices.TryGetValue(product.Sku, out var amount)
                ? Task.FromResult(new Price(amount))
                : throw new KeyNotFoundException($"{product.Sku} is not cached");
    }

    public class LookUpPrice(IAudit audit) : Junction<Product, Price>
    {
        public override Task<Price> Run(Product product)
        {
            audit.Lines.Add($"looked up {product.Sku}");
            return Task.FromResult(new Price(10m));
        }
    }

    public class PriceTrain(IPriceCache cache, IAudit audit) : Train<Product, Price>
    {
        protected override Task<Either<Exception, Price>> Junctions() =>
            AddServices(cache, audit).ShortCircuit<CheckCache>().Chain<LookUpPrice>().Resolve();
    }

    [Test]
    public async Task Run_CacheHit_ReturnsTheCachedPriceButStillRunsTheLookUp()
    {
        var cache = new PriceCache();
        cache.Prices["tea"] = 4m;
        var audit = new Audit();

        var price = await new PriceTrain(cache, audit).Run(new Product("tea"));

        price.Should().Be(new Price(4m));
        audit.Lines.Should().Equal("looked up tea");
    }

    [Test]
    public async Task Run_CacheMiss_IgnoresTheFailureAndReturnsTheLookUp()
    {
        var price = await new PriceTrain(new PriceCache(), new Audit()).Run(new Product("tea"));

        price.Should().Be(new Price(10m));
    }
}
