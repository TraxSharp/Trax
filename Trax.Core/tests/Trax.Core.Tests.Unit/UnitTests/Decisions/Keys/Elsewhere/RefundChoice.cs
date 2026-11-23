using Trax.Core.Decisions;

namespace Trax.Core.Tests.Unit.UnitTests.Decisions.Keys.Elsewhere;

// Another enum of the same name as the one in Keys/Before, asking a different question.
[Asks("Which refund does support offer?")]
public enum RefundChoice
{
    Full,
    Credit,
}
