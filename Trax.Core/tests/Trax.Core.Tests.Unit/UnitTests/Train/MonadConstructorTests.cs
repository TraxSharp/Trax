using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Trax.Core.Extensions;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class MonadConstructorTests : TestSetup
{
    [Test]
    public void Constructor_WithServiceProvider_StoresProviderInMemory()
    {
        var train = new TestTrain();
        var sp = new ServiceCollection().BuildServiceProvider();

        var monad = new Monad<int, string>(train, sp, CancellationToken.None);

        monad.Memory.Should().ContainKey(typeof(IServiceProvider));
        monad.Memory[typeof(IServiceProvider)].Should().BeSameAs(sp);
    }

    [Test]
    public void Constructor_WithoutServiceProvider_DoesNotContainProvider()
    {
        var train = new TestTrain();

        var monad = new Monad<int, string>(train, CancellationToken.None);

        monad.Memory.Should().NotContainKey(typeof(IServiceProvider));
    }

    [Test]
    public void AddServices_MoqMockObject_StoresByMockedInterfaceType()
    {
        var train = new TestTrain();
        var monad = new Monad<int, string>(train, CancellationToken.None).Activate(0);
        var mock = new Mock<IDisposable>();

        monad.AddServices<IDisposable>(mock.Object);

        monad.Memory.Should().ContainKey(typeof(IDisposable));
        monad.Memory[typeof(IDisposable)].Should().BeSameAs(mock.Object);
    }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }
}
