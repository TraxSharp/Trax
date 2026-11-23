using System.Text.Json.Nodes;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>A provider error, as a database that dropped the connection raises it.</summary>
internal sealed class DroppedConnectionException()
    : System.Data.Common.DbException("The connection to the database was lost.");
