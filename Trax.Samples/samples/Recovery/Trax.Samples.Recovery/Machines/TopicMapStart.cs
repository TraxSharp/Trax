using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Topics;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

/// <summary>What a user starts a topic map with: nothing, the wizard starts empty.</summary>
public sealed record TopicMapStart;
