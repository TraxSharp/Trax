using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// The views a host serves from features (ADR <c>Trax.Docs/adr/0058</c>), by name, as its host file declares them:
/// what each reads, the fields it shows and who may read it. The GraphQL surface serves one query field per view.
/// </summary>
internal sealed class MachineViewCatalog(IReadOnlyDictionary<string, HostView> views)
{
    internal IReadOnlyDictionary<string, HostView> Views { get; } = views;
}
