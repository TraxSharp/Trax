using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Extract projects a value out of the object in Memory, so the member it reads has to belong to
/// that object.
/// </summary>
public class ExtractInstanceMemberTests : TestSetup
{
    [Test]
    public void Extract_ReadsTheInstanceProperty_NotAStaticPropertyOfTheSameType()
    {
        var order = new Order { Currency = new Currency("EUR") };

        var monad = new TestTrain().Activate(order);
        monad.Extract<Order, Currency>();

        monad.Exception.Should().BeNull();
        monad
            .Memory[typeof(Currency)]
            .Should()
            .BeSameAs(order.Currency, "the order's own currency is what Extract projects");
    }

    [Test]
    public void Extract_FromATypeWithAnIndexerOfTheTargetType_ReadsThePropertyInsteadOfThrowing()
    {
        var bag = new Bag { Currency = new Currency("EUR") };

        var monad = new BagTrain().Activate(bag);
        var act = () => monad.Extract<Bag, Currency>();

        act.Should().NotThrow("an indexer takes arguments and is not a value to project");
        monad.Exception.Should().BeNull();
        monad.Memory[typeof(Currency)].Should().BeSameAs(bag.Currency);
    }

    [Test]
    public void Extract_FromAValueTypeNotInMemory_RecordsTheMissingType_InsteadOfThrowing()
    {
        var monad = new TestTrain().Activate(new Order { Currency = new Currency("EUR") });
        var act = () => monad.Extract<DateTime, long>();

        act.Should().NotThrow("a missing source is a Left, as it is for a reference type");
        monad.Exception.Should().NotBeNull();
        monad.Exception!.Message.Should().Contain(nameof(DateTime));
    }

    public record Currency(string Code);

    public class Order
    {
        public static Currency Default { get; } = new("USD");

        public Currency Currency { get; init; } = null!;
    }

    public class Bag
    {
        public Currency this[int index] => new("IDX");

        public Currency Currency { get; init; } = null!;
    }

    private class TestTrain : Train<Order, Currency>
    {
        protected override Task<Either<Exception, Currency>> Junctions() =>
            throw new NotImplementedException();
    }

    private class BagTrain : Train<Bag, Currency>
    {
        protected override Task<Either<Exception, Currency>> Junctions() =>
            throw new NotImplementedException();
    }
}
