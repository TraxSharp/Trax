using Reqnroll;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>A step whose check did not hold on the host, and why.</summary>
public sealed class FeatureStepFailedException(string message) : Exception(message);
