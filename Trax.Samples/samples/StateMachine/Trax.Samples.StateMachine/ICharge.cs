using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.StateMachine;

/// <summary>The irreversible effect on <c>Pay</c>: charge the order. Bound inline via <c>RunsOnce&lt;ICharge&gt;</c>.</summary>
public interface ICharge : ISnapshotEffect { }
