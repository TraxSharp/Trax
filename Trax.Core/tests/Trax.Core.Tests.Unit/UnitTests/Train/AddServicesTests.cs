using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class AddServicesTests : TestSetup
{
    [Theory]
    public async Task TestAddServices()
    {
        // Arrange
        var inputService = new TestService();
        var train = new TestTrain();

        var services = new object[] { inputService };
        var serviceTypes = new[] { typeof(ITestService) };

        // Act
        var monad = train.Activate(0);
        monad.AddServices(services, serviceTypes);

        // Assert
        monad.Exception.Should().BeNull();
        monad.Memory.Should().NotBeNull();
        monad.Memory.Should().ContainKey(typeof(ITestService));
        monad.Memory.Should().ContainValue(inputService);
    }

    [Theory]
    public async Task TestInvalidAddServices()
    {
        // Arrange
        var inputService = 1;
        var train = new TestTrain();

        var services = new object[] { inputService };
        var serviceTypes = new[] { typeof(ITestService) };

        // Act
        var monad = train.Activate(0);
        monad.AddServices(services, serviceTypes);

        // Assert
        monad.Exception.Should().NotBeNull();
        monad.Memory.Should().NotBeNull();
    }

    [Theory]
    public async Task TestInvalidAddServicesNoInterface()
    {
        // Arrange
        var inputService = new TestServiceNoInterface();
        var train = new TestTrain();

        var services = new object[] { inputService };
        var serviceTypes = new[] { typeof(ITestService) };

        // Act
        var monad = train.Activate(0);
        monad.AddServices(services, serviceTypes);

        // Assert
        monad.Exception.Should().NotBeNull();
        monad.Memory.Should().NotBeNull();
    }

    [Theory]
    public async Task TestAddServicesOneType()
    {
        // Arrange
        var inputService = new TestService();
        var train = new TestTrain();

        // Act
        var monad = train.Activate(0);
        var baselineCount = monad.Memory.Count;
        monad.AddServices<ITestService>(inputService);

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Exception.Should().BeNull();
        (monad.Memory.Count - baselineCount).Should().Be(1);
    }

    [Theory]
    public async Task TestAddServicesTwoTypes()
    {
        // Arrange
        var service1 = new TestService1();
        var service2 = new TestService2();
        var train = new TestTrain();

        // Act
        var monad = train.Activate(0);
        var baselineCount = monad.Memory.Count;
        monad.AddServices<ITestService1, ITestService2>(service1, service2);

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Exception.Should().BeNull();
        (monad.Memory.Count - baselineCount).Should().Be(2);
    }

    [Theory]
    public async Task TestAddServicesThreeTypes()
    {
        // Arrange
        var service1 = new TestService1();
        var service2 = new TestService2();
        var service3 = new TestService3();
        var train = new TestTrain();

        // Act
        var monad = train.Activate(0);
        var baselineCount = monad.Memory.Count;
        monad.AddServices<ITestService1, ITestService2, ITestService3>(
            service1,
            service2,
            service3
        );

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Exception.Should().BeNull();
        (monad.Memory.Count - baselineCount).Should().Be(3);
    }

    [Theory]
    public async Task TestAddServicesFourTypes()
    {
        // Arrange
        var service1 = new TestService1();
        var service2 = new TestService2();
        var service3 = new TestService3();
        var service4 = new TestService4();
        var train = new TestTrain();

        // Act
        var monad = train.Activate(0);
        var baselineCount = monad.Memory.Count;
        monad.AddServices<ITestService1, ITestService2, ITestService3, ITestService4>(
            service1,
            service2,
            service3,
            service4
        );

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Exception.Should().BeNull();
        (monad.Memory.Count - baselineCount).Should().Be(4);
    }

    [Theory]
    public async Task TestAddServicesFiveTypes()
    {
        // Arrange
        var service1 = new TestService1();
        var service2 = new TestService2();
        var service3 = new TestService3();
        var service4 = new TestService4();
        var service5 = new TestService5();
        var train = new TestTrain();

        // Act
        var monad = train.Activate(0);
        var baselineCount = monad.Memory.Count;
        monad.AddServices<
            ITestService1,
            ITestService2,
            ITestService3,
            ITestService4,
            ITestService5
        >(service1, service2, service3, service4, service5);

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Exception.Should().BeNull();
        (monad.Memory.Count - baselineCount).Should().Be(5);
    }

    [Theory]
    public async Task TestAddServicesSixTypes()
    {
        // Arrange
        var service1 = new TestService1();
        var service2 = new TestService2();
        var service3 = new TestService3();
        var service4 = new TestService4();
        var service5 = new TestService5();
        var service6 = new TestService6();
        var train = new TestTrain();

        // Act
        var monad = train.Activate(0);
        var baselineCount = monad.Memory.Count;
        monad.AddServices<
            ITestService1,
            ITestService2,
            ITestService3,
            ITestService4,
            ITestService5,
            ITestService6
        >(service1, service2, service3, service4, service5, service6);

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Exception.Should().BeNull();
        (monad.Memory.Count - baselineCount).Should().Be(6);
    }

    [Theory]
    public async Task TestAddServicesSevenTypes()
    {
        // Arrange
        var service1 = new TestService1();
        var service2 = new TestService2();
        var service3 = new TestService3();
        var service4 = new TestService4();
        var service5 = new TestService5();
        var service6 = new TestService6();
        var service7 = new TestService7();
        var train = new TestTrain();

        // Act
        var monad = train.Activate(0);
        var baselineCount = monad.Memory.Count;
        monad.AddServices<
            ITestService1,
            ITestService2,
            ITestService3,
            ITestService4,
            ITestService5,
            ITestService6,
            ITestService7
        >(service1, service2, service3, service4, service5, service6, service7);

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Exception.Should().BeNull();
        (monad.Memory.Count - baselineCount).Should().Be(7);
    }

    private sealed class TestServiceNoInterface;

    private sealed class TestService : ITestService;

    private sealed class TestService1 : ITestService1;

    private sealed class TestService2 : ITestService2;

    private sealed class TestService3 : ITestService3;

    private sealed class TestService4 : ITestService4;

    private sealed class TestService5 : ITestService5;

    private sealed class TestService6 : ITestService6;

    private sealed class TestService7 : ITestService7;

    private interface ITestService;

    private interface ITestService1;

    private interface ITestService2;

    private interface ITestService3;

    private interface ITestService4;

    private interface ITestService5;

    private interface ITestService6;

    private interface ITestService7;

    private sealed class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            throw new NotImplementedException();
    }
}
