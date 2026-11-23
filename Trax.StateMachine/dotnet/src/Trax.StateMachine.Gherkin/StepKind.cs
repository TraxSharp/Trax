using Gherkin;
using Gherkin.CucumberMessages;
using Gherkin.CucumberMessages.Pickles;
using Io.Cucumber.Messages.Types;

namespace Trax.StateMachine.Gherkin;

/// <summary>Given, When or Then, with And and But resolved to the keyword before them.</summary>
internal enum StepKind
{
    Given,
    When,
    Then,
}
