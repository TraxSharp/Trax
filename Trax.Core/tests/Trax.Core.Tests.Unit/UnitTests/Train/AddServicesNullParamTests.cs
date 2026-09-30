using FluentAssertions;
using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class AddServicesNullParamTests : TestSetup
{
    [Test]
    public void AddServices_T1_NullService_FailsTheChain()
    {
        var act = Fails(monad => monad.AddServices<ITestService1>(null!));

        act.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void AddServices_T1_NullService_NamesTheServiceType()
    {
        var act = Fails(monad => monad.AddServices<ITestService1>(null!));

        act.Message.Should().StartWith("AddServices<ITestService1> received null");
    }

    [Test]
    public void AddServices_T2_NullSecond_FailsTheChain()
    {
        var act = Fails(monad =>
            monad.AddServices<ITestService1, ITestService2>(new TestService1(), null!)
        );

        act.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void AddServices_T3_NullThird_FailsTheChain()
    {
        var act = Fails(monad =>
            monad.AddServices<ITestService1, ITestService2, ITestService3>(
                new TestService1(),
                new TestService2(),
                null!
            )
        );

        act.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void AddServices_T4_NullFourth_FailsTheChain()
    {
        var act = Fails(monad =>
            monad.AddServices<ITestService1, ITestService2, ITestService3, ITestService4>(
                new TestService1(),
                new TestService2(),
                new TestService3(),
                null!
            )
        );

        act.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void AddServices_T5_NullFifth_FailsTheChain()
    {
        var act = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5
            >(new TestService1(), new TestService2(), new TestService3(), new TestService4(), null!)
        );

        act.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void AddServices_T6_NullSixth_FailsTheChain()
    {
        var act = Fails(monad =>
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
            )
        );

        act.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void AddServices_T7_NullSeventh_FailsTheChain()
    {
        var act = Fails(monad =>
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
            )
        );

        act.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void AddServices_T2_NullFirst_FailsTheChain()
    {
        var act = Fails(monad =>
            monad.AddServices<ITestService1, ITestService2>(null!, new TestService2())
        );

        act.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void AddServices_T3_NullFirst_FailsTheChain()
    {
        var act = Fails(monad =>
            monad.AddServices<ITestService1, ITestService2, ITestService3>(
                null!,
                new TestService2(),
                new TestService3()
            )
        );

        act.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void AddServices_T3_NullMiddle_FailsTheChain()
    {
        var act = Fails(monad =>
            monad.AddServices<ITestService1, ITestService2, ITestService3>(
                new TestService1(),
                null!,
                new TestService3()
            )
        );

        act.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void AddServices_T4_NullEachPosition_FailsTheChain()
    {
        var s1 = new TestService1();
        var s2 = new TestService2();
        var s3 = new TestService3();
        var s4 = new TestService4();

        var a1 = Fails(monad =>
            monad.AddServices<ITestService1, ITestService2, ITestService3, ITestService4>(
                null!,
                s2,
                s3,
                s4
            )
        );
        var a2 = Fails(monad =>
            monad.AddServices<ITestService1, ITestService2, ITestService3, ITestService4>(
                s1,
                null!,
                s3,
                s4
            )
        );
        var a3 = Fails(monad =>
            monad.AddServices<ITestService1, ITestService2, ITestService3, ITestService4>(
                s1,
                s2,
                null!,
                s4
            )
        );

        a1.Should().BeOfType<TrainException>();
        a2.Should().BeOfType<TrainException>();
        a3.Should().BeOfType<TrainException>();
    }

    [Test]
    public void AddServices_T5_NullEachPosition_FailsTheChain()
    {
        var s1 = new TestService1();
        var s2 = new TestService2();
        var s3 = new TestService3();
        var s4 = new TestService4();
        var s5 = new TestService5();

        var a1 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5
            >(null!, s2, s3, s4, s5)
        );
        var a2 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5
            >(s1, null!, s3, s4, s5)
        );
        var a3 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5
            >(s1, s2, null!, s4, s5)
        );
        var a4 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5
            >(s1, s2, s3, null!, s5)
        );

        a1.Should().BeOfType<TrainException>();
        a2.Should().BeOfType<TrainException>();
        a3.Should().BeOfType<TrainException>();
        a4.Should().BeOfType<TrainException>();
    }

    [Test]
    public void AddServices_T6_NullEachPosition_FailsTheChain()
    {
        var s1 = new TestService1();
        var s2 = new TestService2();
        var s3 = new TestService3();
        var s4 = new TestService4();
        var s5 = new TestService5();
        var s6 = new TestService6();

        var a1 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(null!, s2, s3, s4, s5, s6)
        );
        var a2 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(s1, null!, s3, s4, s5, s6)
        );
        var a3 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(s1, s2, null!, s4, s5, s6)
        );
        var a4 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(s1, s2, s3, null!, s5, s6)
        );
        var a5 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6
            >(s1, s2, s3, s4, null!, s6)
        );

        a1.Should().BeOfType<TrainException>();
        a2.Should().BeOfType<TrainException>();
        a3.Should().BeOfType<TrainException>();
        a4.Should().BeOfType<TrainException>();
        a5.Should().BeOfType<TrainException>();
    }

    [Test]
    public void AddServices_T7_NullEachPosition_FailsTheChain()
    {
        var s1 = new TestService1();
        var s2 = new TestService2();
        var s3 = new TestService3();
        var s4 = new TestService4();
        var s5 = new TestService5();
        var s6 = new TestService6();
        var s7 = new TestService7();

        var a1 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(null!, s2, s3, s4, s5, s6, s7)
        );
        var a2 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(s1, null!, s3, s4, s5, s6, s7)
        );
        var a3 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(s1, s2, null!, s4, s5, s6, s7)
        );
        var a4 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(s1, s2, s3, null!, s5, s6, s7)
        );
        var a5 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(s1, s2, s3, s4, null!, s6, s7)
        );
        var a6 = Fails(monad =>
            monad.AddServices<
                ITestService1,
                ITestService2,
                ITestService3,
                ITestService4,
                ITestService5,
                ITestService6,
                ITestService7
            >(s1, s2, s3, s4, s5, null!, s7)
        );

        a1.Should().BeOfType<TrainException>();
        a2.Should().BeOfType<TrainException>();
        a3.Should().BeOfType<TrainException>();
        a4.Should().BeOfType<TrainException>();
        a5.Should().BeOfType<TrainException>();
        a6.Should().BeOfType<TrainException>();
    }

    /// <summary>
    /// Runs <paramref name="add"/> on a fresh chain and returns what it recorded. A null service
    /// fails the chain like every other invalid argument instead of throwing out of it.
    /// </summary>
    private static Exception Fails(Func<Monad<int, string>, Monad<int, string>> add)
    {
        var monad = new TestTrain().Activate(0);

        var returned = add(monad);

        returned.Should().BeSameAs(monad);
        monad.Exception.Should().BeOfType<TrainException>();
        monad
            .Memory.Keys.Should()
            .NotContain(t => t.IsInterface && t.Name.StartsWith("ITestService"));

        return monad.Exception!;
    }

    [Test]
    public void AddServices_NullService_NamesItsPositionAndType()
    {
        var failure = Fails(monad =>
            monad.AddServices<ITestService1, ITestService2, ITestService3>(
                new TestService1(),
                null!,
                new TestService3()
            )
        );

        failure.Message.Should().Contain("ITestService2");
        failure.Message.Should().Contain("position 2 of 3");
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
        protected override Task<Either<Exception, string>> Junctions() =>
            throw new NotImplementedException();
    }
}
