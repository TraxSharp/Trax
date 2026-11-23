using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>Normalises to the field <c>map2d</c> as well.</summary>
[TraxMachine]
public sealed class Map2dMachine() : NamedStepMachine("map2d");
