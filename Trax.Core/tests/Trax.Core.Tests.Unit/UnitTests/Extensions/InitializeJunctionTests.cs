using System.Reflection;
using FluentAssertions;
using LanguageExt;
using Trax.Core.Extensions;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class InitializeJunctionTests : TestSetup
{
    [Theory]
    public async Task TestInitializeJunction()
    {
        // Arrange
        var input = 1;

        var train = new TestTrain();
        var monad = train.Activate(1);

        // Act
        var junction = monad.InitializeJunction<TestValidJunction, int, string>();

        // Assert
        junction.Should().NotBeNull();

        var result = await junction!.Run(input);
        result.Should().Be(input.ToString());
    }

    [Theory]
    public async Task TestInvalidInitializeJunction()
    {
        // Arrange
        var train = new TestTrain();
        var monad = train.Activate(1);

        // Act
        var junction = monad.InitializeJunction<TestInvalidJunction, int, string>();

        // Assert
        monad.Exception.Should().NotBeNull();
    }

    /// <summary>
    /// A junction whose constructor throws surfaces its own exception, not the reflection wrapper.
    /// </summary>
    /// <remarks>
    /// The constructor is reached through <c>ConstructorInfo.Invoke</c>, which wraps anything it
    /// throws in a <see cref="TargetInvocationException"/> whose message is only "Exception has been
    /// thrown by the target of an invocation." Left wrapped, the train records that string as the
    /// failure reason and the failure classifier is handed <c>TargetInvocationException</c> rather
    /// than the type the junction actually threw.
    /// </remarks>
    [Test]
    public void InitializeJunction_ConstructorThrows_SurfacesTheJunctionsOwnException()
    {
        var monad = new TestTrain().Activate(1);

        var act = () => monad.InitializeJunction<ThrowingConstructorJunction, int, string>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                ThrowingConstructorJunction.Reason,
                "the operator needs the junction's reason, not the reflection wrapper's"
            );
    }

    /// <summary>
    /// The same unwrap is what lets a cancelled construction be recorded as a cancellation.
    /// </summary>
    /// <remarks>
    /// A train decides between <c>Cancelled</c> and <c>Failed</c> by testing the exception for
    /// <see cref="OperationCanceledException"/>. Wrapped, it is never one, so a junction cancelled
    /// while being built was recorded as a failure, which a manifest counts toward retries.
    /// </remarks>
    [Test]
    public void InitializeJunction_ConstructorCancels_SurfacesTheCancellation()
    {
        var monad = new TestTrain().Activate(1);

        var act = () => monad.InitializeJunction<CancellingConstructorJunction, int, string>();

        act.Should().Throw<OperationCanceledException>();
    }

    private class ThrowingConstructorJunction : Junction<int, string>
    {
        public const string Reason = "the junction could not be built";

        public ThrowingConstructorJunction() => throw new InvalidOperationException(Reason);

        public override Task<string> Run(int input) => Task.FromResult(input.ToString());
    }

    private class CancellingConstructorJunction : Junction<int, string>
    {
        public CancellingConstructorJunction() => throw new OperationCanceledException();

        public override Task<string> Run(int input) => Task.FromResult(input.ToString());
    }

    private class TestTrain : Train<int, string>
    {
        protected override async Task<Either<Exception, string>> Junctions() => Resolve();
    }

    private class TestValidJunction : Junction<int, string>
    {
        public override async Task<string> Run(int input)
        {
            return input.ToString();
        }
    }

#pragma warning disable CS9113 // Parameter is unread - intentionally invalid junction for testing
    private class TestInvalidJunction(int _intInput, string _stringInput) { }
#pragma warning restore CS9113
}
