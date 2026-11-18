using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>The order machine's irreversible effect port (bound inline via <c>RunsOnce&lt;IOrderCharge&gt;</c>).</summary>
public interface IOrderCharge : ISnapshotEffect;
