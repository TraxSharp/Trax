using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>Its output, whose fields the host reads by name: <c>result</c> before <c>shots</c>.</summary>
public sealed record AimOutput(string Shots, string Result);
