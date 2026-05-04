using FluentAssertions;
using LanguageExt;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class AddServicesNullParamTests : TestSetup
{
    [Test]
    public void AddServices_T1_NullService_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () => monad.AddServices<ITestService1>(null!);

        act.Should().Throw<Exception>().WithMessage("*cannot be null*");
    }

    [Test]
    public void AddServices_T2_NullSecond_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () =>
            monad.AddServices<ITestService1, ITestService2>(new TestService1(), null!);

        act.Should().Throw<Exception>().WithMessage("*cannot be null*");
    }

    [Test]
    public void AddServices_T3_NullThird_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () =>
            monad.AddServices<ITestService1, ITestService2, ITestService3>(
                new TestService1(),
                new TestService2(),
                null!
            );

        act.Should().Throw<Exception>().WithMessage("*cannot be null*");
    }

    [Test]
    public void AddServices_T4_NullFourth_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () =>
            monad.AddServices<ITestService1, ITestService2, ITestService3, ITestService4>(
                new TestService1(),
                new TestService2(),
                new TestService3(),
                null!
            );

        act.Should().Throw<Exception>().WithMessage("*cannot be null*");
    }

    [Test]
    public void AddServices_T5_NullFifth_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5
            >(
                new TestService1(),
                new TestService2(),
                new TestService3(),
                new TestService4(),
                null!
            );

        act.Should().Throw<Exception>().WithMessage("*cannot be null*");
    }

    [Test]
    public void AddServices_T6_NullSixth_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(
                new TestService1(),
                new TestService2(),
                new TestService3(),
                new TestService4(),
                new TestService5(),
                null!
            );

        act.Should().Throw<Exception>().WithMessage("*cannot be null*");
    }

    [Test]
    public void AddServices_T7_NullSeventh_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(
                new TestService1(),
                new TestService2(),
                new TestService3(),
                new TestService4(),
                new TestService5(),
                new TestService6(),
                null!
            );

        act.Should().Throw<Exception>().WithMessage("*cannot be null*");
    }

    [Test]
    public void AddServices_T2_NullFirst_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () =>
            monad.AddServices<ITestService1, ITestService2>(null!, new TestService2());

        act.Should().Throw<Exception>().WithMessage("*cannot be null*");
    }

    [Test]
    public void AddServices_T3_NullFirst_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () =>
            monad.AddServices<ITestService1, ITestService2, ITestService3>(
                null!,
                new TestService2(),
                new TestService3()
            );

        act.Should().Throw<Exception>().WithMessage("*cannot be null*");
    }

    [Test]
    public void AddServices_T3_NullMiddle_Throws()
    {
        var monad = new TestTrain().Activate(0);

        Action act = () =>
            monad.AddServices<ITestService1, ITestService2, ITestService3>(
                new TestService1(),
                null!,
                new TestService3()
            );

        act.Should().Throw<Exception>().WithMessage("*cannot be null*");
    }

    [Test]
    public void AddServices_T4_NullEachPosition_Throws()
    {
        var monad = new TestTrain().Activate(0);
        var s1 = new TestService1();
        var s2 = new TestService2();
        var s3 = new TestService3();
        var s4 = new TestService4();

        Action a1 = () =>
            monad.AddServices<ITestService1, ITestService2, ITestService3, ITestService4>(
                null!,
                s2,
                s3,
                s4
            );
        Action a2 = () =>
            monad.AddServices<ITestService1, ITestService2, ITestService3, ITestService4>(
                s1,
                null!,
                s3,
                s4
            );
        Action a3 = () =>
            monad.AddServices<ITestService1, ITestService2, ITestService3, ITestService4>(
                s1,
                s2,
                null!,
                s4
            );

        a1.Should().Throw<Exception>();
        a2.Should().Throw<Exception>();
        a3.Should().Throw<Exception>();
    }

    [Test]
    public void AddServices_T5_NullEachPosition_Throws()
    {
        var monad = new TestTrain().Activate(0);
        var s1 = new TestService1();
        var s2 = new TestService2();
        var s3 = new TestService3();
        var s4 = new TestService4();
        var s5 = new TestService5();

        Action a1 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5
            >(null!, s2, s3, s4, s5);
        Action a2 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5
            >(s1, null!, s3, s4, s5);
        Action a3 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5
            >(s1, s2, null!, s4, s5);
        Action a4 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5
            >(s1, s2, s3, null!, s5);

        a1.Should().Throw<Exception>();
        a2.Should().Throw<Exception>();
        a3.Should().Throw<Exception>();
        a4.Should().Throw<Exception>();
    }

    [Test]
    public void AddServices_T6_NullEachPosition_Throws()
    {
        var monad = new TestTrain().Activate(0);
        var s1 = new TestService1();
        var s2 = new TestService2();
        var s3 = new TestService3();
        var s4 = new TestService4();
        var s5 = new TestService5();
        var s6 = new TestService6();

        Action a1 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(null!, s2, s3, s4, s5, s6);
        Action a2 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(s1, null!, s3, s4, s5, s6);
        Action a3 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(s1, s2, null!, s4, s5, s6);
        Action a4 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(s1, s2, s3, null!, s5, s6);
        Action a5 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(s1, s2, s3, s4, null!, s6);

        a1.Should().Throw<Exception>();
        a2.Should().Throw<Exception>();
        a3.Should().Throw<Exception>();
        a4.Should().Throw<Exception>();
        a5.Should().Throw<Exception>();
    }

    [Test]
    public void AddServices_T7_NullEachPosition_Throws()
    {
        var monad = new TestTrain().Activate(0);
        var s1 = new TestService1();
        var s2 = new TestService2();
        var s3 = new TestService3();
        var s4 = new TestService4();
        var s5 = new TestService5();
        var s6 = new TestService6();
        var s7 = new TestService7();

        Action a1 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(null!, s2, s3, s4, s5, s6, s7);
        Action a2 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(s1, null!, s3, s4, s5, s6, s7);
        Action a3 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(s1, s2, null!, s4, s5, s6, s7);
        Action a4 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(s1, s2, s3, null!, s5, s6, s7);
        Action a5 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(s1, s2, s3, s4, null!, s6, s7);
        Action a6 = () =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(s1, s2, s3, s4, s5, null!, s7);

        a1.Should().Throw<Exception>();
        a2.Should().Throw<Exception>();
        a3.Should().Throw<Exception>();
        a4.Should().Throw<Exception>();
        a5.Should().Throw<Exception>();
        a6.Should().Throw<Exception>();
    }

    private interface ITestService1 { }

    private interface ITestService2 { }

    private interface ITestService3 { }

    private interface ITestService4 { }

    private interface ITestService5 { }

    private interface ITestService6 { }

    private interface ITestService7 { }

    private class TestService1 : ITestService1 { }

    private class TestService2 : ITestService2 { }

    private class TestService3 : ITestService3 { }

    private class TestService4 : ITestService4 { }

    private class TestService5 : ITestService5 { }

    private class TestService6 : ITestService6 { }

    private class TestService7 : ITestService7 { }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }
}
