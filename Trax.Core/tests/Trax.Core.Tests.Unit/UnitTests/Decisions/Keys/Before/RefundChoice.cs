using Trax.Core.Decisions;

namespace Trax.Core.Tests.Unit.UnitTests.Decisions.Keys.Before;

// The enum before its namespace is renamed. Keys/Renamed holds it after, Keys/Elsewhere another of the same name.
[Asks("How should the refund be paid?")]
public enum RefundChoice
{
    Full,
    Credit,
}
