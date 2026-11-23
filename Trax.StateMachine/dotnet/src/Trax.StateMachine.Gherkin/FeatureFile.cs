using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>One feature file: its path, as provenance and every problem names it, and its text.</summary>
public sealed record FeatureFile(string Path, string Text);
