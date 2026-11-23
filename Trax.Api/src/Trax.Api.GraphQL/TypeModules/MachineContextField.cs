using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Trax.Api.GraphQL.Validation;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>One field of a machine's context type: its JSON name, its GraphQL type and how it is read.</summary>
/// <param name="Name">The field's name, which is its JSON name in the stored context.</param>
/// <param name="ClrType">The type the declaring record gives it.</param>
internal sealed record MachineContextField(string Name, Type ClrType)
{
    /// <summary>The field's GraphQL type: the CLR type's, always nullable, since a state need not hold it.</summary>
    public Type GraphQLType =>
        ClrType.IsValueType && Nullable.GetUnderlyingType(ClrType) is null
            ? typeof(Nullable<>).MakeGenericType(ClrType)
            : ClrType;

    /// <summary>The field's value in <paramref name="context"/>, read as the record declares it, or null.</summary>
    public object? Read(JsonObject context) =>
        context.TryGetPropertyValue(Name, out var node) && node is not null
            ? node.Deserialize(GraphQLType, MachineSurface.Json)
            : null;
}
