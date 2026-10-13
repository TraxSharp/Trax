using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Trax.Api.StateMachine.E2E;

/// <summary>Reads an <see cref="E2EHost"/>'s built schema, for the tests about its shape.</summary>
public static class SchemaWalk
{
    public const string RootQuery = "RootQuery";
    public const string RootMutation = "RootMutation";

    /// <summary>The host's request executor, which builds the schema on first use.</summary>
    public static Task<IRequestExecutor> ExecutorAsync(this IHost host) =>
        host
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax")
            .AsTask();

    /// <summary>The object type named <paramref name="name"/>, or null.</summary>
    public static ObjectType? ObjectTypeNamed(IRequestExecutor executor, string name) =>
        executor.Schema.Types.OfType<ObjectType>().SingleOrDefault(t => t.Name == name);

    /// <summary>The object type <paramref name="field"/> of <paramref name="type"/> resolves to, or null.</summary>
    public static ObjectType? FieldType(ObjectType? type, string field) =>
        type is not null && type.Fields.TryGetField(field, out var f)
            ? f.Type.NamedType() as ObjectType
            : null;

    /// <summary>
    /// The type <c>machines.&lt;machine&gt;</c> resolves to under <paramref name="root"/>, or null when there is no
    /// such field.
    /// </summary>
    public static ObjectType? MachineType(IRequestExecutor executor, string root, string machine) =>
        FieldType(FieldType(ObjectTypeNamed(executor, root), "machines"), machine);

    /// <summary>The named types reachable from <paramref name="start"/>, through fields and their arguments.</summary>
    public static IReadOnlyCollection<ITypeDefinition> Reachable(ObjectType start)
    {
        var seen = new Dictionary<string, ITypeDefinition>(StringComparer.Ordinal);
        var queue = new Queue<ITypeDefinition>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!seen.TryAdd(type.Name, type))
                continue;
            switch (type)
            {
                case ObjectType o:
                    foreach (var f in o.Fields.Where(f => !f.IsIntrospectionField))
                    {
                        queue.Enqueue(f.Type.NamedType());
                        foreach (var a in f.Arguments)
                            queue.Enqueue(a.Type.NamedType());
                    }
                    break;
                case InputObjectType i:
                    foreach (var f in i.Fields)
                        queue.Enqueue(f.Type.NamedType());
                    break;
            }
        }
        return seen.Values;
    }
}
