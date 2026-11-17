using Trax.Core.Decisions;

namespace Trax.Core.Tests.Unit.UnitTests.Decisions.Keys.Elsewhere;

public static class Keyed
{
    [Asks("Which refund does support offer?", Key = "support_refund")]
    public enum RefundChoice
    {
        Full,
        Credit,
    }
}
