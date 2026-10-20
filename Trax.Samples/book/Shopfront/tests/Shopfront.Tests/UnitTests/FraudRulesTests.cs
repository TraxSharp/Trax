using Shopfront.Fraud;
using Trax.Core.Decisions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class FraudRulesTests
{
    // The question as a Gate<OrderRisk, LooksFraudulent> asks it.
    private static DecisionRequest Asking(OrderRisk risk) =>
        new(
            "PlaceOrder",
            risk,
            [new YesNoQuestion(QuestionKey.For<LooksFraudulent>(), "Does this order look like fraud?", null, null)]
        );

    private static async Task<double> ProbabilityOfYes(IDecider decider, OrderRisk risk)
    {
        var result = await decider.Decide(Asking(risk), CancellationToken.None);
        return ((YesNoAnswer)result.Answers[QuestionKey.For<LooksFraudulent>()]).Probability;
    }

    [TestCase(99.99, true, 0.0)]
    [TestCase(100.00, true, 1.0)]
    [TestCase(500.00, false, 0.0)]
    public async Task Live_HoldsFirstOrdersFrom100(decimal total, bool firstOrder, double yes) =>
        (await ProbabilityOfYes(FraudRules.Live(), new OrderRisk("ada", total, firstOrder)))
            .Should()
            .Be(yes);

    [TestCase(59.99, true, 0.0)]
    [TestCase(60.00, true, 1.0)]
    public async Task Stricter_HoldsFirstOrdersFrom60(decimal total, bool firstOrder, double yes) =>
        (await ProbabilityOfYes(new StricterFraudRules(), new OrderRisk("ada", total, firstOrder)))
            .Should()
            .Be(yes);
}
