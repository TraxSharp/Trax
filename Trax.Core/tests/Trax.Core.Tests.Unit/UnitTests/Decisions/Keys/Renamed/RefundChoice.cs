using Trax.Core.Decisions;

namespace Trax.Core.Tests.Unit.UnitTests.Decisions.Keys.Renamed;

// The same enum as Keys/Before, after its namespace is renamed.
[Asks("How should the refund be paid?")]
public enum RefundChoice
{
    Full,
    Credit,
}
