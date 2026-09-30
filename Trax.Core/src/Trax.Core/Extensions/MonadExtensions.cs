using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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
internal static class MonadExtensions
{
    /// <summary>
    /// Cache for junction constructor info and parameter types, keyed by junction type.
    /// </summary>
    private static readonly ConcurrentDictionary<
        Type,
        (ConstructorInfo Constructor, Type[] ParameterTypes)?
    > ConstructorCache = new();

    /// <summary>
    /// Initializes a junction instance by extracting its constructor parameters from Memory.
    /// </summary>
    public static TJunction? InitializeJunction<TJunction, TInput, TReturn>(
        this Monad<TInput, TReturn> monad
    )
        where TJunction : class
    {
        var junctionType = typeof(TJunction);

        var cached = ConstructorCache.GetOrAdd(
            junctionType,
            type =>
            {
                if (JunctionConstructorProblem(type) is not null)
                    return null;

                var constructors = type.GetConstructors();

                var parameterTypes = constructors[0]
                    .GetParameters()
                    .Select(x => x.ParameterType)
                    .ToArray();

                return (constructors[0], parameterTypes);
            }
        );

        if (cached is null)
        {
            monad.Exception ??= new TrainException(
                JunctionConstructorProblem(junctionType, monad.Train.GetType())!
            );
            return null;
        }

        var (constructor, constructorArguments) = cached.Value;

        // Extract the constructor parameters from Memory, naming the junction when one is missing:
        // the type alone does not say which junction asked for it, or whether it was expected
        // from an earlier junction or from the container.
        var constructorParameters = monad.ExtractTypesFromMemory(
            constructorArguments,
            missing => MissingConstructorArgumentMessage(junctionType, missing, monad.Train)
        );

        if (monad.Exception is not null)
            return null;

        // Create an instance of the junction. Invoke wraps whatever the constructor threw in a
        // TargetInvocationException, whose message says only that an invocation target threw.
        // Unwrapped here so the train records the junction's own reason and the failure classifier
        // is handed the type it actually threw: left wrapped, a constructor that cancels is never
        // an OperationCanceledException, so the run was recorded Failed rather than Cancelled and a
        // manifest counted it toward retries. TrainExecutionService unwraps its own reflection call
        // sites the same way.
        TJunction? initializedJunction;

        try
        {
            initializedJunction = (TJunction?)constructor.Invoke(constructorParameters);
        }
        catch (TargetInvocationException ex)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
            throw;
        }

        if (initializedJunction is null)
        {
            monad.Exception ??= new TrainException(
                $"Could not invoke constructor for ({junctionType})."
            );
            return null;
        }

        return initializedJunction;
    }

    /// <summary>
    /// Extracts multiple types from Memory.
    /// </summary>
    public static dynamic?[] ExtractTypesFromMemory<TInput, TReturn>(
        this Monad<TInput, TReturn> monad,
        IEnumerable<Type> types,
        Func<Type, string>? missingMessage = null
    )
    {
        var typeArray = types as Type[] ?? types.ToArray();
        var result = new dynamic?[typeArray.Length];
        for (var i = 0; i < typeArray.Length; i++)
            result[i] = monad.ExtractTypeFromMemory(typeArray[i], missingMessage);
        return result;
    }

    /// <summary>
    /// Extracts a value of type T from Memory.
    /// </summary>
    public static T? ExtractTypeFromMemory<T, TInput, TReturn>(
        this Monad<TInput, TReturn> monad,
        Func<Type, string>? missingMessage = null
    )
    {
        var type = monad.ExtractTypeFromMemory(typeof(T), missingMessage);

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
            throw new TrainException(
                $"Could not find ILoggerFactory for input type: ({tIn}). Have you injected an ILoggerFactory into the Monad's services?"
            );

        var generics = tIn.GetGenericArguments();

        if (generics.Length != 1)
            throw new TrainException(
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
    /// Extracts a value from Memory by its type, falling back to the container and then to the
    /// logger factory. When nothing supplies it, the chain fails with
    /// <paramref name="missingMessage"/>'s text, which should say who needed the value.
    /// </summary>
    public static dynamic? ExtractTypeFromMemory<TInput, TReturn>(
        this Monad<TInput, TReturn> monad,
        Type tIn,
        Func<Type, string>? missingMessage = null
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
                throw new TrainException(
                    missingMessage?.Invoke(tIn) ?? MissingValueMessage(tIn, monad.Train)
                );

            return input;
        }
        catch (Exception e)
        {
            monad.Exception ??= e;
            return null;
        }
    }

    /// <summary>
    /// Why Trax cannot build <paramref name="junctionType"/> from its constructor, or null when
    /// it can. Trax builds a junction through its one public constructor, so anything else fails
    /// every run; the chain recorder asks the same question so the host refuses it at startup.
    /// </summary>
    internal static string? JunctionConstructorProblem(Type junctionType, Type? trainType = null)
    {
        var train = trainType is null ? "" : $" (train '{trainType.ReadableName()}')";

        if (!junctionType.IsClass)
            return $"Junction '{junctionType.ReadableName()}'{train} must be a class; Trax builds "
                + "a junction from its class. Chain the class that implements it, or use IChain "
                + "to resolve it by its interface.";

        if (junctionType.IsAbstract)
            return $"Junction '{junctionType.ReadableName()}'{train} is abstract, so Trax cannot "
                + "build it. Chain a concrete class.";

        var count = junctionType.GetConstructors().Length;

        if (count == 1)
            return null;

        return $"Junction '{junctionType.ReadableName()}'{train} has {count} public constructors; "
            + "Trax builds a junction through its single public constructor. Give it exactly one.";
    }

    internal static string MissingConstructorArgumentMessage<TInput, TReturn>(
        Type junctionType,
        Type missing,
        Train.Train<TInput, TReturn> train
    ) =>
        $"Junction '{junctionType.ReadableName()}' (train '{train.GetType().ReadableName()}') "
        + $"needs '{missing.ReadableName()}' as a constructor argument, but nothing earlier in "
        + "the chain produced one and it is not registered in the container. Register it or "
        + "chain a junction that outputs it first.";

    internal static string MissingJunctionInputMessage<TInput, TReturn>(
        Type junctionType,
        Type missing,
        Train.Train<TInput, TReturn> train
    ) =>
        $"Junction '{junctionType.ReadableName()}' (train '{train.GetType().ReadableName()}') "
        + $"needs '{missing.ReadableName()}' as its input, but nothing earlier in the chain "
        + "produced one and it is not registered in the container. Chain a junction that "
        + "outputs it first, or register it.";

    private static string MissingValueMessage<TInput, TReturn>(
        Type missing,
        Train.Train<TInput, TReturn> train
    ) =>
        $"Train '{train.GetType().ReadableName()}' needs '{missing.ReadableName()}', but nothing "
        + "earlier in the chain produced one and it is not registered in the container.";

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
            0 => throw new TrainException($"Cannot have Tuple of length 0."),
            1 => throw new TrainException(
                "Tuple of a single length should be passed as the value itself."
            ),
            2 => TypeHelpers.ConvertTwoTuple(typeTuples),
            3 => TypeHelpers.ConvertThreeTuple(typeTuples),
            4 => TypeHelpers.ConvertFourTuple(typeTuples),
            5 => TypeHelpers.ConvertFiveTuple(typeTuples),
            6 => TypeHelpers.ConvertSixTuple(typeTuples),
            7 => TypeHelpers.ConvertSevenTuple(typeTuples),
            _ => throw new TrainException($"Could not create Tuple for type ({inputType})"),
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
            throw new TrainException(
                $"({typeof(TIn)}) is not a Tuple but was attempted to be extracted as one."
            );

        if (input is null)
            throw new TrainException($"Input of type ({typeof(TIn)} cannot be null.");

        var inputTuple = (ITuple)input;

        if (inputTuple.Length > 7)
            throw new TrainException(
                $"Tuple input ({typeof(TIn)}) cannot have a length greater than 7."
            );

        // The declared element types, when the caller's static type is the tuple itself. A
        // junction taking a declared element type, a base class say, finds the value whatever
        // subtype it holds; the startup chain check assumes exactly that.
        var declaredTypes = typeof(TIn).IsTuple() ? typeof(TIn).GetGenericArguments() : [];

        for (var i = 0; i < inputTuple.Length; i++)
        {
            var tupleValue = inputTuple[i];

            // A null element has no value to find; a junction asking for it fails with the
            // ordinary "could not find type" rather than a NullReferenceException here.
            if (tupleValue is null)
                continue;

            if (i < declaredTypes.Length)
                monad.Memory[declaredTypes[i]] = tupleValue;

            var tupleValueType = tupleValue.GetType();

            monad.Memory[tupleValueType] = tupleValue;

            var tupleValueTypeInterfaces = tupleValueType.GetInterfaces();
            foreach (var tupleValueTypeInterface in tupleValueTypeInterfaces)
                monad.Memory[tupleValueTypeInterface] = tupleValue;
        }

        return Unit.Default;
    }
}
