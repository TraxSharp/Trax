using System.Text.RegularExpressions;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>How one person of one scenario calls the host: the headers each of their requests carries.</summary>
public sealed record FeatureCredential(IReadOnlyDictionary<string, string> Headers);
