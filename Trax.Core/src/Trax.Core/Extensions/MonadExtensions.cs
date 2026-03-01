using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Monad;
using Trax.Core.Utils;

namespace Trax.Core.Extensions;

/// <summary>
/// Provides extension methods for working with Monad instances.
/// These methods enable dependency injection, type extraction, and tuple handling.
/// </summary>
public static class MonadExtensions
{
    /// <summary>
    /// Cache for step constructor info and parameter types, keyed by step type.
    /// </summary>
    private static readonly ConcurrentDictionary<
        Type,
        (ConstructorInfo Constructor, Type[] ParameterTypes)?
    > ConstructorCache = new();

    /// <summary>
    /// Initializes a step instance by extracting its constructor parameters from Memory.
    /// </summary>
    public static TStep? InitializeStep<TStep, TInput, TReturn>(this Monad<TInput, TReturn> monad)
        where TStep : class
    {
        var stepType = typeof(TStep);

        var cached = ConstructorCache.GetOrAdd(
            stepType,
            type =>
            {
                if (!type.IsClass)
                    return null;

                var constructors = type.GetConstructors();
                if (constructors.Length != 1)
                    return null;

                var parameterTypes = constructors[0]
                    .GetParameters()
                    .Select(x => x.ParameterType)
                    .ToArray();

                return (constructors[0], parameterTypes);
            }
        );

        if (cached is null)
        {
            if (!stepType.IsClass)
                monad.Exception ??= new WorkflowException($"Step ({stepType}) must be a class.");
            else
                monad.Exception ??= new WorkflowException(
                    $"Step classes can only have a single constructor ({stepType})."
                );
            return null;
        }

        var (constructor, constructorArguments) = cached.Value;

        // Extract the constructor parameters from Memory
        var constructorParameters = monad.ExtractTypesFromMemory(constructorArguments);

        if (monad.Exception is not null)
            return null;

        // Create an instance of the step
        var initializedStep = (TStep?)constructor.Invoke(constructorParameters);

        if (initializedStep is null)
        {
            monad.Exception ??= new WorkflowException(
                $"Could not invoke constructor for ({stepType})."
            );
            return null;
        }

        return initializedStep;
    }

    /// <summary>
    /// Extracts multiple types from Memory.
    /// </summary>
    public static dynamic?[] ExtractTypesFromMemory<TInput, TReturn>(
        this Monad<TInput, TReturn> monad,
        IEnumerable<Type> types
    )
    {
        var typeArray = types as Type[] ?? types.ToArray();
        var result = new dynamic?[typeArray.Length];
        for (var i = 0; i < typeArray.Length; i++)
            result[i] = monad.ExtractTypeFromMemory(typeArray[i]);
        return result;
    }

    /// <summary>
    /// Extracts a value of type T from Memory.
    /// </summary>
    public static T? ExtractTypeFromMemory<T, TInput, TReturn>(this Monad<TInput, TReturn> monad)
    {
        var type = monad.ExtractTypeFromMemory(typeof(T));

        return type is null ? default : (T)type;
    }

    /// <summary>
    /// Extracts a logger from a logger factory.
    /// </summary>
    internal static dynamic? ExtractLoggerFromLoggerFactory<TInput, TReturn>(
        this Monad<TInput, TReturn> monad,
        Type tIn
    )
    {
        if (tIn.IsGenericType == false || tIn.GetGenericTypeDefinition() != typeof(ILogger<>))
            return null;

        if (
            monad.Memory.GetValueOrDefault(typeof(ILoggerFactory))
            is not ILoggerFactory loggerFactory
        )
            throw new WorkflowException(
                $"Could not find ILoggerFactory for input type: ({tIn}). Have you injected an ILoggerFactory into the Monad's services?"
            );

        var generics = tIn.GetGenericArguments();

        if (generics.Length != 1)
            throw new WorkflowException(
                $"Incorrect number of generic arguments for input type ({tIn}). Found ({generics.Length}) generics with types ({string.Join(", ", generics.Select(x => x.Name))})."
            );

        return loggerFactory.CreateGenericLogger(generics.First());
    }

    /// <summary>
    /// Extracts a service from a service provider in Memory.
    /// </summary>
    internal static dynamic? ExtractTypeFromServiceProvider<TInput, TReturn>(
        this Monad<TInput, TReturn> monad,
        Type tIn
    )
    {
        var service = monad.Memory.GetValueOrDefault(typeof(IServiceProvider))
            is IServiceProvider serviceProvider
            ? serviceProvider.GetService(tIn)
            : null;

        return service;
    }

    /// <summary>
    /// Extracts a value from Memory by its type.
    /// </summary>
    public static dynamic? ExtractTypeFromMemory<TInput, TReturn>(
        this Monad<TInput, TReturn> monad,
        Type tIn
    )
    {
        try
        {
            var input = tIn.IsTuple()
                ? monad.ExtractTuple(tIn)
                : monad.Memory.GetValueOrDefault(tIn)
                    ?? monad.ExtractTypeFromServiceProvider(tIn)
                    ?? monad.ExtractLoggerFromLoggerFactory(tIn);

            if (input is null)
                throw new WorkflowException($"Could not find type: ({tIn}).");

            return input;
        }
        catch (Exception e)
        {
            monad.Exception ??= e;
            return null;
        }
    }

    /// <summary>
    /// Extracts a tuple from Memory.
    /// </summary>
    public static dynamic ExtractTuple<TInput, TReturn>(
        this Monad<TInput, TReturn> monad,
        Type inputType
    )
    {
        var typeTuples = TypeHelpers.ExtractTypeTuples(monad.Memory, inputType);

        return typeTuples.Count switch
        {
            0 => throw new WorkflowException($"Cannot have Tuple of length 0."),
            1 => throw new WorkflowException(
                "Tuple of a single length should be passed as the value itself."
            ),
            2 => TypeHelpers.ConvertTwoTuple(typeTuples),
            3 => TypeHelpers.ConvertThreeTuple(typeTuples),
            4 => TypeHelpers.ConvertFourTuple(typeTuples),
            5 => TypeHelpers.ConvertFiveTuple(typeTuples),
            6 => TypeHelpers.ConvertSixTuple(typeTuples),
            7 => TypeHelpers.ConvertSevenTuple(typeTuples),
            _ => throw new WorkflowException($"Could not create Tuple for type ({inputType})"),
        };
    }

    /// <summary>
    /// Adds a tuple to Memory by extracting its components.
    /// </summary>
    public static Unit AddTupleToMemory<TIn, TInput, TReturn>(
        this Monad<TInput, TReturn> monad,
        TIn input
    )
    {
        if (!typeof(TIn).IsTuple())
            throw new WorkflowException(
                $"({typeof(TIn)}) is not a Tuple but was attempted to be extracted as one."
            );

        if (input is null)
            throw new WorkflowException($"Input of type ({typeof(TIn)} cannot be null.");

        var inputTuple = (ITuple)input;

        if (inputTuple.Length > 7)
            throw new WorkflowException(
                $"Tuple input ({typeof(TIn)}) cannot have a length greater than 7."
            );

        var tupleList = Enumerable.Range(0, inputTuple.Length).Select(i => inputTuple[i]!).ToList();

        foreach (var tupleValue in tupleList)
        {
            var tupleValueType = tupleValue.GetType();

            monad.Memory[tupleValueType] = tupleValue;

            var tupleValueTypeInterfaces = tupleValueType.GetInterfaces();
            foreach (var tupleValueTypeInterface in tupleValueTypeInterfaces)
                monad.Memory[tupleValueTypeInterface] = tupleValue;
        }

        return Unit.Default;
    }
}
