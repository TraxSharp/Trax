using FluentAssertions;
using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class MonadExtensionsTupleTests : TestSetup
{
    [Test]
    public void ExtractTuple_FourElements_ReturnsValueTuple()
    {
        var monad = NewActivated();
        monad.AddServices<IA>(new A());
        monad.AddServices<IB>(new B());
        monad.AddServices<IC>(new C());
        monad.AddServices<ID>(new D());

        var tuple = monad.ExtractTuple(typeof(ValueTuple<IA, IB, IC, ID>));

        ((object)tuple).Should().NotBeNull();
    }

    [Test]
    public void ExtractTuple_FiveElements_ReturnsValueTuple()
    {
        var monad = NewActivated();
        monad.AddServices<IA>(new A());
        monad.AddServices<IB>(new B());
        monad.AddServices<IC>(new C());
        monad.AddServices<ID>(new D());
        monad.AddServices<IE>(new E());

        var tuple = monad.ExtractTuple(typeof(ValueTuple<IA, IB, IC, ID, IE>));

        ((object)tuple).Should().NotBeNull();
    }

    [Test]
    public void ExtractTuple_SixElements_ReturnsValueTuple()
    {
        var monad = NewActivated();
        monad.AddServices<IA>(new A());
        monad.AddServices<IB>(new B());
        monad.AddServices<IC>(new C());
        monad.AddServices<ID>(new D());
        monad.AddServices<IE>(new E());
        monad.AddServices<IF>(new F());

        var tuple = monad.ExtractTuple(typeof(ValueTuple<IA, IB, IC, ID, IE, IF>));

        ((object)tuple).Should().NotBeNull();
    }

    [Test]
    public void ExtractTuple_SevenElements_ReturnsValueTuple()
    {
        var monad = NewActivated();
        monad.AddServices<IA>(new A());
        monad.AddServices<IB>(new B());
        monad.AddServices<IC>(new C());
        monad.AddServices<ID>(new D());
        monad.AddServices<IE>(new E());
        monad.AddServices<IF>(new F());
        monad.AddServices<IG>(new G());

        var tuple = monad.ExtractTuple(typeof(ValueTuple<IA, IB, IC, ID, IE, IF, IG>));

        ((object)tuple).Should().NotBeNull();
    }

    private static Trax.Core.Monad.Monad<int, string> NewActivated() => new TestTrain().Activate(0);

    private interface IA { }

    private interface IB { }

    private interface IC { }

    private interface ID { }

    private interface IE { }

    private interface IF { }

    private interface IG { }

    private class A : IA { }

    private class B : IB { }

    private class C : IC { }

    private class D : ID { }

    private class E : IE { }

    private class F : IF { }

    private class G : IG { }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }
}
