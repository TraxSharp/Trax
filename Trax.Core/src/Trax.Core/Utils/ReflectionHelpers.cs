using System.Collections.Concurrent;
using System.Reflection;
using LanguageExt;
using Trax.Core.Junction;
using Trax.Core.Monad;

namespace Trax.Core.Utils;

/// <summary>
/// Provides helper methods for working with reflection.
/// These methods are used throughout Trax.Core to dynamically invoke methods and extract type information.
/// Results are cached in static ConcurrentDictionary instances to avoid repeated reflection lookups.
/// </summary>
internal static class ReflectionHelpers
{
    /// <summary>
    /// Cache for junction type arguments (TIn, TOut) keyed by the junction type.
    /// Since a junction's IJunction&lt;TIn, TOut&gt; interface never changes, this is safe to cache statically.
    /// </summary>
    private static readonly ConcurrentDictionary<
        Type,
        (Type TIn, Type TOut)
    > JunctionTypeArgumentsCache = new();

    /// <summary>
    /// Cache for resolved generic MethodInfo instances, keyed by (ownerType, methodName, junctionType, tIn, tOut, paramCount).
    /// </summary>
    private static readonly ConcurrentDictionary<
        (Type OwnerType, string MethodName, Type JunctionType, Type TIn, Type TOut, int ParamCount),
        MethodInfo
    > GenericMethodCache = new();

    /// <summary>
    /// Extracts the input and output type arguments from an IJunction implementation.
    /// Results are cached per junction type for subsequent calls.
    /// </summary>
    /// <typeparam name="TJunction">The junction type</typeparam>
    /// <returns>A tuple containing the input type and output type</returns>
    /// <exception cref="InvalidOperationException">Thrown if TJunction does not implement IJunction&lt;TIn, TOut&gt;</exception>
    internal static (Type, Type) ExtractJunctionTypeArguments<TJunction>()
    {
        var junctionType = typeof(TJunction);

        if (JunctionTypeArgumentsCache.TryGetValue(junctionType, out var cached))
            return cached;

        // Find the IJunction<,> interface
        var interfaceType = junctionType
            .GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJunction<,>)
            );

        if (interfaceType is null)
        {
            throw new InvalidOperationException(
                $"{nameof(TJunction)} does not implement IJunction<TIn, TOut>."
            );
        }

        // Extract the generic arguments
        var types = interfaceType.GetGenericArguments();
        var result = (types[0], types[1]);

        JunctionTypeArgumentsCache.TryAdd(junctionType, result);

        return result;
    }

    /// <summary>
    /// Finds the ShortCircuitJunction method that matches the specified types and parameter count.
    /// Results are cached per (monadType, junctionType, tIn, tOut, paramCount) combination.
    /// </summary>
    internal static MethodInfo FindGenericShortCircuitJunctionMethod<TJunction, TInput, TReturn>(
        Monad<TInput, TReturn> monad,
        Type tIn,
        Type tOut,
        int parameterCount
    )
    {
        var cacheKey = (
            monad.GetType(),
            "ShortCircuitJunction",
            typeof(TJunction),
            tIn,
            tOut,
            parameterCount
        );

        if (GenericMethodCache.TryGetValue(cacheKey, out var cached))
            return cached;

        return GenericMethodCache.GetOrAdd(
            cacheKey,
            _ =>
            {
                var methods = monad
                    .GetType()
                    .GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    )
                    .Where(m =>
                        m is { Name: "ShortCircuitJunction", IsGenericMethodDefinition: true }
                    )
                    .Where(m => m.GetGenericArguments().Length == 3)
                    .Where(m => m.GetParameters().Length == parameterCount)
                    .ToList();

                switch (methods.Count)
                {
                    case > 1:
                        throw new InvalidOperationException(
                            "More than one Generic 'ShortCircuitJunction' method found."
                        );
                    case 0:
                        throw new InvalidOperationException(
                            "Suitable 'ShortCircuitJunction' method not found."
                        );
                }

                return methods.First().MakeGenericMethod(typeof(TJunction), tIn, tOut);
            }
        );
    }

    /// <summary>
    /// Finds the ChainJunction method that matches the specified types and parameter count.
    /// Results are cached per (monadType, junctionType, tIn, tOut, paramCount) combination.
    /// </summary>
    internal static MethodInfo FindGenericChainJunctionMethod<TJunction, TInput, TReturn>(
        Monad<TInput, TReturn> monad,
        Type tIn,
        Type tOut,
        int parameterCount
    )
    {
        var cacheKey = (
            monad.GetType(),
            "ChainJunction",
            typeof(TJunction),
            tIn,
            tOut,
            parameterCount
        );

        if (GenericMethodCache.TryGetValue(cacheKey, out var cached))
            return cached;

        return GenericMethodCache.GetOrAdd(
            cacheKey,
            _ =>
            {
                var methods = monad
                    .GetType()
                    .GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    )
                    .Where(m => m is { Name: "ChainJunction", IsGenericMethodDefinition: true })
                    .Where(m => m.GetGenericArguments().Length == 3)
                    .Where(m => m.GetParameters().Length == parameterCount)
                    .ToList();

                switch (methods.Count)
                {
                    case > 1:
                        throw new InvalidOperationException(
                            "More than one Generic 'ChainJunction' method found."
                        );
                    case 0:
                        throw new InvalidOperationException(
                            "Suitable 'ChainJunction' method not found."
                        );
                }

                return methods.First().MakeGenericMethod(typeof(TJunction), tIn, tOut);
            }
        );
    }

    /// <summary>
    /// Extracts the Right value from a dynamic Either object.
    /// </summary>
    internal static Option<dynamic> GetRightFromDynamicEither(dynamic eitherObject)
    {
        var eitherType = eitherObject.GetType();

        if (eitherType.IsGenericType && eitherType.GetGenericTypeDefinition() == typeof(Either<,>))
        {
            var isRightProp = eitherType.GetProperty("IsRight");
            var isRight = (bool)isRightProp.GetValue(eitherObject, null);

            if (isRight)
            {
                var rightValueProp = eitherType.GetProperty("Case");
                var rightValue = rightValueProp.GetValue(eitherObject, null);
                return rightValue;
            }
        }

        return Option<dynamic>.None;
    }
}
