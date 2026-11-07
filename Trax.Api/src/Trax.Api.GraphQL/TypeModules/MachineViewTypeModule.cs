using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Builds the <c>views</c> root (ADR <c>Trax.Docs/adr/0058</c>): one query field per view a host serves from features.
/// In each, a user machine the view reads is a list of the caller's own instances, its <c>id</c> and the view's fields
/// of it, and a system machine is the one instance at its key. A view is read only by its readers, the actors the
/// features show reading it: every other caller is refused <c>FORBIDDEN</c> before anything is read. A state is shown
/// as a String, its label when the view gives one; a context field has the type its machine's record gives it.
/// </summary>
internal sealed class MachineViewTypeModule(
    IEnumerable<IMachine> machines,
    MachineViewCatalog catalog
) : TypeModule
{
    internal const string RootField = "views";
    internal const string QueriesType = "ViewsQueries";

    public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken
    )
    {
        var types = new List<ITypeSystemMember>
        {
            NamespaceTypes.Base(QueriesType),
            NamespaceTypes.Field("RootQuery", RootField, QueriesType),
        };
        var byName = machines.ToDictionary(m => m.Name, StringComparer.Ordinal);

        var fields = new List<(string Field, string Type, HostView View)>();
        foreach (var view in catalog.Views.Values.OrderBy(v => v.Name, StringComparer.Ordinal))
        {
            var pascal = Pascal(view.Name);
            if (!System.Text.RegularExpressions.Regex.IsMatch(pascal, @"\A[A-Za-z][A-Za-z0-9]*\z"))
                throw new InvalidOperationException(
                    $"The view '{view.Name}' has no GraphQL name: a view's name is words of letters and digits, the first "
                        + "starting with a letter."
                );
            var viewType = $"{pascal}View";
            foreach (var read in view.Reads)
                types.Add(RowType(view, read, pascal, byName[read.Machine]));
            types.Add(
                new ObjectType(d =>
                {
                    d.Name(viewType);
                    d.Description($"The view '{view.Name}': what it reads, for the caller.");
                    foreach (var read in view.Reads)
                    {
                        var row = RowTypeName(pascal, read.Machine);
                        var field = d.Field(MachineSurface.FieldName(read.Machine));
                        if (read.Key is null)
                            field
                                .Description($"The caller's own instances of '{read.Machine}'.")
                                .Type(
                                    new NonNullTypeNode(
                                        new ListTypeNode(
                                            new NonNullTypeNode(new NamedTypeNode(row))
                                        )
                                    )
                                )
                                .Resolve(ctx =>
                                    ((JsonArray)ctx.Parent<JsonObject>()[read.Machine]!)
                                        .Select(n => n!.AsObject())
                                        .ToList()
                                );
                        else
                            field
                                .Description(
                                    $"The instance of '{read.Machine}' at {read.Key}, or null before it is started."
                                )
                                .Type(new NamedTypeNode(row))
                                .Resolve(ctx =>
                                    ctx.Parent<JsonObject>()[read.Machine] as JsonObject
                                );
                    }
                })
            );
            fields.Add((Camel(pascal), viewType, view));
        }

        types.Add(
            new ObjectTypeExtension(d =>
            {
                d.Name(QueriesType);
                foreach (var (field, type, view) in fields)
                    d.Field(field)
                        .Authorize(ApplyPolicy.BeforeResolver)
                        .Description(
                            $"Reads the view '{view.Name}' for the caller, if the caller is one of its readers."
                        )
                        .Type(new NonNullTypeNode(new NamedTypeNode(type)))
                        .Resolve(ctx => ReadAsync(ctx, view));
            })
        );
        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    // One instance's row: its id, and the view's fields of its machine, each typed as the machine's record types it.
    private static ObjectType RowType(HostView view, ViewRead read, string pascal, IMachine machine)
    {
        var context = MachineContextFields
            .Of(machine, machine.States)
            .ToDictionary(f => f.Name, StringComparer.Ordinal);
        return new ObjectType(d =>
        {
            d.Name(RowTypeName(pascal, read.Machine));
            d.Description($"One instance of '{read.Machine}' as the view '{view.Name}' shows it.");
            d.Field("id")
                .Type<NonNullType<UuidType>>()
                .Resolve(ctx => Guid.Parse(ctx.Parent<JsonObject>()["id"]!.GetValue<string>()));
            foreach (var field in view.Fields.Where(f => f.Machine == read.Machine))
            {
                if (field.IsState || !context.TryGetValue(field.Source, out var typed))
                    d.Field(field.Name)
                        .Type<StringType>()
                        .Resolve(ctx =>
                            ctx.Parent<JsonObject>()[field.Name] is JsonValue v
                            && v.GetValueKind() == JsonValueKind.String
                                ? v.GetValue<string>()
                                : ctx.Parent<JsonObject>()[field.Name]?.ToJsonString()
                        );
                else
                    d.Field(field.Name)
                        .Type(typed.GraphQLType)
                        .Resolve(ctx =>
                            ctx.Parent<JsonObject>()[field.Name] is { } node
                                ? node.Deserialize(typed.GraphQLType, MachineSurface.Json)
                                : null
                        );
            }
        });
    }

    // The read rule first, then the caller's own rows and the system's. A caller who is not a reader is refused before
    // anything is read.
    private static async Task<JsonObject> ReadAsync(IResolverContext ctx, HostView view)
    {
        var userKey = ctx.Services.GetService<ISnapshotPrincipal>()?.CurrentUserKey;
        if (string.IsNullOrWhiteSpace(userKey))
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("Sign in to read this.")
                    .SetCode("UNAUTHENTICATED")
                    .Build()
            );
        if (!view.Readers.IsAnyAuthenticated)
        {
            var caller = ctx.Services.GetRequiredService<ICallerAuthorization>();
            foreach (var set in view.Readers.RoleSets)
                if (!set.Any(caller.IsInRole))
                    throw new GraphQLException(
                        ErrorBuilder
                            .New()
                            .SetMessage("You may not do this.")
                            .SetCode("FORBIDDEN")
                            .Build()
                    );
        }
        return await MachineViewReader.Read(
            ctx.Services.GetRequiredService<IDataContext>(),
            view,
            userKey,
            ctx.RequestAborted
        );
    }

    private static string RowTypeName(string view, string machine) =>
        $"{view}{MachineSurface.PascalName(machine)}Row";

    // "shop sign" is ShopSign, and its field shopSign.
    private static string Pascal(string name) =>
        string.Concat(
            name.Split(' ', '-', '_')
                .Where(w => w.Length > 0)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..])
        );

    private static string Camel(string pascal) => char.ToLowerInvariant(pascal[0]) + pascal[1..];
}
