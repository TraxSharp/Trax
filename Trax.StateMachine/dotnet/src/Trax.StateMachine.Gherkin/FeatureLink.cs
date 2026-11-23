using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>An arrow on the board, from one sticky's <see cref="FeatureSticky.Id"/> to another's.</summary>
public sealed record FeatureLink(string From, string To);
