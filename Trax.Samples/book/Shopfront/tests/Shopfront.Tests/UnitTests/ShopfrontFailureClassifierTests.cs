using Microsoft.EntityFrameworkCore;
using Shopfront.Failures;
using Shopfront.Payments;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ShopfrontFailureClassifierTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData(new TrainException("CAP is out of stock."), FailureClass.Permanent);
        yield return new TestCaseData(new TimeoutException(), FailureClass.Transient);
        yield return new TestCaseData(new DbUpdateConcurrencyException(), FailureClass.Transient);
        yield return new TestCaseData(new PaymentDeclinedException("Declined."), null);
        yield return new TestCaseData(new InvalidOperationException(), null);
    }

    [TestCaseSource(nameof(Cases))]
    public void Classify_Exception_GivesItsClass(Exception exception, FailureClass? expected) =>
        new ShopfrontFailureClassifier().Classify(exception).Should().Be(expected);
}
