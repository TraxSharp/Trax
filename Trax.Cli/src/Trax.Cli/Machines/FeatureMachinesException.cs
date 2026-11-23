using System.Text;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;

namespace Trax.Cli.Machines;

/// <summary>A path or input <see cref="FeatureDirectory"/> cannot use, worded for the terminal.</summary>
internal sealed class FeatureMachinesException(string message) : Exception(message);
