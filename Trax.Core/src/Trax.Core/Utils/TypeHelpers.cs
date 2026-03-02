using Trax.Core.Exceptions;

namespace Trax.Core.Utils;

/// <summary>
/// Provides helper methods for working with types.
/// These methods are used throughout Trax.Core to handle tuple types and dynamic type conversion.
/// </summary>
internal static class TypeHelpers
{
    /// <summary>
    /// Extracts types and values from Memory to create a tuple.
    /// </summary>
    /// <param name="memory">The Memory dictionary</param>
    /// <param name="inputType">The tuple type</param>
    /// <returns>A list of type-value pairs</returns>
    /// <exception cref="TrainException">Thrown if a type is not found in Memory</exception>
    /// <remarks>
    /// This method is used to extract the components of a tuple type from Memory.
    /// It's used by the Extract method to create tuples from individual components.
    /// </remarks>
    internal static List<(Type, dynamic)> ExtractTypeTuples(
        Dictionary<Type, object> memory,
        Type inputType
    )
    {
        var dynamicList = new List<(Type, dynamic)>();

        // Extract each generic argument from the tuple type
        foreach (var type in inputType.GenericTypeArguments)
        {
            var value = memory.GetValueOrDefault(type);

            // Ensure the value exists in Memory
            if (value is null)
                throw new TrainException(
                    $"Could not extract type ({type}) from Tuple. Value not in Memory."
                );

            dynamicList.Add((type, value));
        }

        return dynamicList;
    }

    /// <summary>
    /// Creates a tuple with two elements.
    /// </summary>
    /// <param name="dynamicList">The list of type-value pairs</param>
    /// <returns>A tuple with two elements</returns>
    /// <exception cref="ArgumentNullException">Thrown if dynamicList is null</exception>
    /// <exception cref="ArgumentException">Thrown if dynamicList has fewer than two elements</exception>
    /// <remarks>
    /// This method is used to create a tuple with two elements from a list of type-value pairs.
    /// It's used by the Extract method to create tuples from individual components.
    /// </remarks>
    internal static dynamic ConvertTwoTuple(List<(Type, dynamic)> dynamicList)
    {
        ArgumentNullException.ThrowIfNull(dynamicList);

        if (dynamicList.Count < 2)
            throw new ArgumentException(
                "List must have at least two elements.",
                nameof(dynamicList)
            );

        var (type1, value1) = dynamicList[0];
        var (type2, value2) = dynamicList[1];

        // Create a tuple with the specified types and values
        var tupleType = typeof(ValueTuple<,>).MakeGenericType(type1, type2);
        return Activator.CreateInstance(tupleType, value1, value2);
    }

    /// <summary>
    /// Creates a tuple with three elements.
    /// </summary>
    /// <param name="dynamicList">The list of type-value pairs</param>
    /// <returns>A tuple with three elements</returns>
    /// <exception cref="ArgumentNullException">Thrown if dynamicList is null</exception>
    /// <exception cref="ArgumentException">Thrown if dynamicList has fewer than three elements</exception>
    /// <remarks>
    /// This method is used to create a tuple with three elements from a list of type-value pairs.
    /// It's used by the Extract method to create tuples from individual components.
    /// </remarks>
    internal static dynamic ConvertThreeTuple(List<(Type, dynamic)> dynamicList)
    {
        ArgumentNullException.ThrowIfNull(dynamicList);

        if (dynamicList.Count < 3)
            throw new ArgumentException(
                "List must have at least three elements.",
                nameof(dynamicList)
            );

        var (type1, value1) = dynamicList[0];
        var (type2, value2) = dynamicList[1];
        var (type3, value3) = dynamicList[2];

        // Create a tuple with the specified types and values
        var tupleType = typeof(ValueTuple<,,>).MakeGenericType(type1, type2, type3);
        return Activator.CreateInstance(tupleType, value1, value2, value3);
    }

    /// <summary>
    /// Creates a tuple with four elements.
    /// </summary>
    /// <param name="dynamicList">The list of type-value pairs</param>
    /// <returns>A tuple with four elements</returns>
    /// <exception cref="ArgumentNullException">Thrown if dynamicList is null</exception>
    /// <exception cref="ArgumentException">Thrown if dynamicList has fewer than four elements</exception>
    /// <remarks>
    /// This method is used to create a tuple with four elements from a list of type-value pairs.
    /// It's used by the Extract method to create tuples from individual components.
    /// </remarks>
    internal static dynamic ConvertFourTuple(List<(Type, dynamic)> dynamicList)
    {
        ArgumentNullException.ThrowIfNull(dynamicList);

        if (dynamicList.Count < 4)
            throw new ArgumentException(
                "List must have at least four elements.",
                nameof(dynamicList)
            );

        var (type1, value1) = dynamicList[0];
        var (type2, value2) = dynamicList[1];
        var (type3, value3) = dynamicList[2];
        var (type4, value4) = dynamicList[3];

        // Create a tuple with the specified types and values
        var tupleType = typeof(ValueTuple<,,,>).MakeGenericType(type1, type2, type3, type4);
        return Activator.CreateInstance(tupleType, value1, value2, value3, value4);
    }

    /// <summary>
    /// Creates a tuple with five elements.
    /// </summary>
    /// <param name="dynamicList">The list of type-value pairs</param>
    /// <returns>A tuple with five elements</returns>
    /// <exception cref="ArgumentNullException">Thrown if dynamicList is null</exception>
    /// <exception cref="ArgumentException">Thrown if dynamicList has fewer than five elements</exception>
    /// <remarks>
    /// This method is used to create a tuple with five elements from a list of type-value pairs.
    /// It's used by the Extract method to create tuples from individual components.
    /// </remarks>
    internal static dynamic ConvertFiveTuple(List<(Type, dynamic)> dynamicList)
    {
        ArgumentNullException.ThrowIfNull(dynamicList);

        if (dynamicList.Count < 5)
            throw new ArgumentException(
                "List must have at least five elements.",
                nameof(dynamicList)
            );

        var (type1, value1) = dynamicList[0];
        var (type2, value2) = dynamicList[1];
        var (type3, value3) = dynamicList[2];
        var (type4, value4) = dynamicList[3];
        var (type5, value5) = dynamicList[4];

        // Create a tuple with the specified types and values
        var tupleType = typeof(ValueTuple<,,,,>).MakeGenericType(type1, type2, type3, type4, type5);
        return Activator.CreateInstance(tupleType, value1, value2, value3, value4, value5);
    }

    /// <summary>
    /// Creates a tuple with six elements.
    /// </summary>
    /// <param name="dynamicList">The list of type-value pairs</param>
    /// <returns>A tuple with six elements</returns>
    /// <exception cref="ArgumentNullException">Thrown if dynamicList is null</exception>
    /// <exception cref="ArgumentException">Thrown if dynamicList has fewer than six elements</exception>
    /// <remarks>
    /// This method is used to create a tuple with six elements from a list of type-value pairs.
    /// It's used by the Extract method to create tuples from individual components.
    /// </remarks>
    internal static dynamic ConvertSixTuple(List<(Type, dynamic)> dynamicList)
    {
        ArgumentNullException.ThrowIfNull(dynamicList);

        if (dynamicList.Count < 6)
            throw new ArgumentException(
                "List must have at least six elements.",
                nameof(dynamicList)
            );

        var (type1, value1) = dynamicList[0];
        var (type2, value2) = dynamicList[1];
        var (type3, value3) = dynamicList[2];
        var (type4, value4) = dynamicList[3];
        var (type5, value5) = dynamicList[4];
        var (type6, value6) = dynamicList[5];

        // Create a tuple with the specified types and values
        var tupleType = typeof(ValueTuple<,,,,,>).MakeGenericType(
            type1,
            type2,
            type3,
            type4,
            type5,
            type6
        );
        return Activator.CreateInstance(tupleType, value1, value2, value3, value4, value5, value6);
    }

    /// <summary>
    /// Creates a tuple with seven elements.
    /// </summary>
    /// <param name="dynamicList">The list of type-value pairs</param>
    /// <returns>A tuple with seven elements</returns>
    /// <exception cref="ArgumentNullException">Thrown if dynamicList is null</exception>
    /// <exception cref="ArgumentException">Thrown if dynamicList has fewer than seven elements</exception>
    /// <remarks>
    /// This method is used to create a tuple with seven elements from a list of type-value pairs.
    /// It's used by the Extract method to create tuples from individual components.
    /// </remarks>
    internal static dynamic ConvertSevenTuple(List<(Type, dynamic)> dynamicList)
    {
        ArgumentNullException.ThrowIfNull(dynamicList);

        if (dynamicList.Count < 7)
            throw new ArgumentException(
                "List must have at least seven elements.",
                nameof(dynamicList)
            );

        var (type1, value1) = dynamicList[0];
        var (type2, value2) = dynamicList[1];
        var (type3, value3) = dynamicList[2];
        var (type4, value4) = dynamicList[3];
        var (type5, value5) = dynamicList[4];
        var (type6, value6) = dynamicList[5];
        var (type7, value7) = dynamicList[6];

        // Create a tuple with the specified types and values
        var tupleType = typeof(ValueTuple<,,,,,,>).MakeGenericType(
            type1,
            type2,
            type3,
            type4,
            type5,
            type6,
            type7
        );
        return Activator.CreateInstance(
            tupleType,
            value1,
            value2,
            value3,
            value4,
            value5,
            value6,
            value7
        );
    }
}
