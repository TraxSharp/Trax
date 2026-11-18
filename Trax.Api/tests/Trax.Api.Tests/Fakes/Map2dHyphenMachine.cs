using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

// A machine id is kebab-case, so the only ids that normalise to one camelCase field are those a hyphen before a
// digit separates: "map-2d" and "map2d" both become map2d. An id starting with a digit is refused by the builder.

/// <summary>Normalises to the field <c>map2d</c>.</summary>
[TraxMachine]
public sealed class Map2dHyphenMachine() : NamedStepMachine("map-2d");
