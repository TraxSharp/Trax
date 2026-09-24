using FluentAssertions;
using Trax.Core.Exceptions;

namespace Trax.Core.Tests.Unit.UnitTests.Exceptions;

/// <summary>
/// The message a train gets when its chain declaration reads per-execution state.
///
/// <para>Trax.Effect throws this from <c>TrainInput</c> and <c>TrainOutput</c>, and it fails the
/// host's startup check, so its message is the only thing the author of the offending train
/// sees. It has to say which train, which member, and where the work belongs instead.</para>
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
public class ChainDeclarationExceptionTests : TestSetup
{
    [Test]
    public void Message_NamesTheTrainAndTheMemberItRead()
    {
        var exception = new ChainDeclarationException("CreateOrderTrain", "TrainInput");

        exception
            .Message.Should()
            .StartWith(
                "CreateOrderTrain read 'TrainInput' while declaring its chain",
                "the reader has to find the train and the line without a stack trace"
            );
    }

    [Test]
    public void Message_SendsTheWorkThatNeedsTheMemberIntoAJunction()
    {
        var exception = new ChainDeclarationException("CreateOrderTrain", "TrainOutput");

        exception
            .Message.Should()
            .Contain(
                "Move the work that needs 'TrainOutput' into a junction",
                "the fix is to move the work, not to find another way to read the value"
            )
            .And.Contain("cannot be verified at startup");
    }
}
