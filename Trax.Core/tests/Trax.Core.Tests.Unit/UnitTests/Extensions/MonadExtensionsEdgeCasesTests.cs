using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class MonadExtensionsEdgeCasesTests : TestSetup
{
    [Test]
    public void InitializeJunction_NonClass_RecordsTrainException()
    {
        var monad = new TestTrain().Activate(0);

        var result = monad.InitializeJunction<INotAClass, int, string>();

        result.Should().BeNull();
        monad.Exception.Should().BeOfType<TrainException>();
        monad.Exception!.Message.Should().Contain("must be a class");
    }

    [Test]
    public void InitializeJunction_MultipleConstructors_RecordsTrainException()
    {
        var monad = new TestTrain().Activate(0);

        var result = monad.InitializeJunction<MultiCtorJunction, int, string>();

        result.Should().BeNull();
        monad.Exception.Should().BeOfType<TrainException>();
        monad.Exception!.Message.Should().Contain("single constructor");
    }

    [Test]
    public void InitializeJunction_MissingDependencyInMemory_RecordsExceptionViaExtract()
    {
        var monad = new TestTrain().Activate(0);

        var result = monad.InitializeJunction<NeedsDependencyJunction, int, string>();

        result.Should().BeNull();
        monad.Exception.Should().NotBeNull();
    }

    [Test]
    public void ExtractLoggerFromLoggerFactory_NonGenericILogger_ReturnsNull()
    {
        var monad = new TestTrain().Activate(0);

        var result = monad.ExtractLoggerFromLoggerFactory(typeof(string));

        ((object?)result).Should().BeNull();
    }

    [Test]
    public void ExtractLoggerFromLoggerFactory_NoLoggerFactoryInMemory_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () =>
            monad.ExtractLoggerFromLoggerFactory(typeof(ILogger<MonadExtensionsEdgeCasesTests>));

        act.Should().Throw<TrainException>().WithMessage("*ILoggerFactory*");
    }

    [Test]
    public void ExtractLoggerFromLoggerFactory_CreatesGenericLogger_FromFactory()
    {
        var factory = NullLoggerFactory.Instance;
        var monad = new TestTrain().Activate(0).AddServices<ILoggerFactory>(factory);

        var logger = monad.ExtractLoggerFromLoggerFactory(
            typeof(ILogger<MonadExtensionsEdgeCasesTests>)
        );

        ((object?)logger).Should().NotBeNull();
    }

    [Test]
    public void ExtractTuple_TwoElements_ReturnsValueTuple()
    {
        var monad = new TestTrain().Activate(0);
        monad.AddServices<IFoo>(new Foo());
        monad.AddServices<IBar>(new Bar());

        var tuple = monad.ExtractTuple(typeof(ValueTuple<IFoo, IBar>));

        ((object)tuple).Should().NotBeNull();
    }

    [Test]
    public void ExtractTuple_OneElement_Throws()
    {
        var monad = new TestTrain().Activate(0);
        monad.AddServices<IFoo>(new Foo());

        Action act = () => monad.ExtractTuple(typeof(ValueTuple<IFoo>));

        act.Should().Throw<TrainException>().WithMessage("*single length*");
    }

    [Test]
    public void ExtractTuple_TooManyElements_Throws()
    {
        // ValueTuple of 8 has TRest, ExtractTypeTuples would not fill it sensibly,
        // but we exercise the default switch arm by passing a non-tuple-shaped type
        // that yields zero matched fields. Use Object which has no fields/properties.
        var monad = new TestTrain().Activate(0);

        Action act = () => monad.ExtractTuple(typeof(object));

        // ExtractTypeTuples returns 0 matched types for object → "Tuple of length 0"
        act.Should().Throw<TrainException>();
    }

    private interface INotAClass { }

    private interface IFoo { }

    private interface IBar { }

    private class Foo : IFoo { }

    private class Bar : IBar { }

    private class MultiCtorJunction
    {
        public MultiCtorJunction() { }

        public MultiCtorJunction(int x) { }
    }

    private class NeedsDependencyJunction
    {
        public NeedsDependencyJunction(IFoo foo) { }
    }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }
}
