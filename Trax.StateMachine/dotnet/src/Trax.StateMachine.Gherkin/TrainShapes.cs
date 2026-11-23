using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.Core.Extensions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin.Lexical;

namespace Trax.StateMachine.Gherkin;

/// <summary>A train's input and output fields from its records, found in assemblies by its full name.</summary>
internal static class TrainShapes
{
    private const string ServiceTrain = "Trax.Effect.Services.ServiceTrain.IServiceTrain`2";

    internal static TrainShape Find(IReadOnlyCollection<Assembly> assemblies, string name)
    {
        try
        {
            return Reflect(assemblies, name);
        }
        catch (Exception ex)
            when (ex
                    is TypeLoadException
                        or FileNotFoundException
                        or FileLoadException
                        or BadImageFormatException
                        or ReflectionTypeLoadException
            )
        {
            // A hub built against a dependency its bin does not have, or on a framework this process does not carry
            // (ASP.NET Core), has types that do not load here.
            return new TrainShape(
                Input: null,
                Output: null,
                $"{name} could not be loaded from the assemblies given: {ex.Message} Build the hub, and give its "
                    + "output's assemblies, or write the train's fields on 'Returns:' and 'Takes:' lines instead."
            );
        }
    }

    private static TrainShape Reflect(IReadOnlyCollection<Assembly> assemblies, string name)
    {
        var found = assemblies
            .Select(a => a.GetType(name, throwOnError: false))
            .OfType<Type>()
            .Distinct()
            .ToList();
        if (found.Count == 0)
            return new TrainShape(
                Input: null,
                Output: null,
                $"no train {name} is in the assemblies given ({string.Join(", ", assemblies.Select(a => a.GetName().Name))})."
            );
        if (found.Count > 1)
            return new TrainShape(
                Input: null,
                Output: null,
                $"{name} is in more than one of the assemblies given."
            );
        var train = found[0];
        // Compared by name, not identity: the CLI loads a hub's assemblies beside its own copy of Trax.Effect.
        var service = new[] { train }
            .Concat(train.GetInterfaces())
            .FirstOrDefault(i =>
                i.IsGenericType
                && string.Equals(
                    i.GetGenericTypeDefinition().FullName,
                    ServiceTrain,
                    StringComparison.Ordinal
                )
            );
        if (service is null)
            return new TrainShape(
                Input: null,
                Output: null,
                $"{name} is not a train interface (IServiceTrain<TIn, TOut>)."
            );
        var arguments = service.GetGenericArguments();
        try
        {
            return new TrainShape(Fields(arguments[0]), Fields(arguments[1]), Problem: null);
        }
        catch (InvalidOperationException ex)
        {
            return new TrainShape(
                Input: null,
                Output: null,
                $"{name}'s records have no schema: {ex.Message}"
            );
        }
    }

    private static JsonArray Fields(Type record) =>
        (JsonArray)
            IrExporter
                .WriteSchema(SchemaReflection.ForInput(record))["fields"]
                .NotNull()
                .DeepClone();
}
