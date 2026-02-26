using System.Reflection;

namespace Trax.Core.Extensions;

/// <summary>
/// Provides extension methods for working with Moq mock objects.
/// These methods enable Trax.Core to integrate with Moq for testing.
/// </summary>
public static class MoqExtensions
{
    /// <summary>
    /// Determines whether a type is a Moq proxy type.
    /// </summary>
    /// <param name="type">The type to check</param>
    /// <returns>True if the type is a Moq proxy, false otherwise</returns>
    /// <remarks>
    /// Moq creates proxy types at runtime that inherit from the mocked type.
    /// This method checks if a type is one of these proxies by examining its base type's assembly.
    /// </remarks>
    public static bool IsMoqProxy(this Type type)
    {
        return type.BaseType?.Assembly.FullName != null
            && type.BaseType.Assembly.FullName.Split(", ").FirstOrDefault() == "Moq";
    }

    /// <summary>
    /// Gets the mocked type from a Moq mock object.
    /// </summary>
    /// <param name="mockedObject">The mock object</param>
    /// <returns>The type that was mocked</returns>
    /// <exception cref="ArgumentNullException">Thrown if mockedObject is null</exception>
    /// <remarks>
    /// This method uses reflection to extract the mocked type from a Moq mock object.
    /// It's used in AddServices to register mocks by their mocked interface type.
    /// </remarks>
    public static Type GetMockedTypeFromObject(this object mockedObject)
    {
        if (mockedObject is null)
            throw new ArgumentNullException(nameof(mockedObject));

        // Get the Mock property from the proxy object
        var mockField = mockedObject
            .GetType()
            .GetProperty("Mock", BindingFlags.Instance | BindingFlags.Public);
        if (mockField is null)
            return null;

        // Get the Mock instance
        var mock = mockField.GetValue(mockedObject);
        if (mock is null)
            return null;

        // Get the MockedType property from the Mock instance
        var mockedTypeProperty = mock.GetType()
            .GetProperty("MockedType", BindingFlags.Instance | BindingFlags.NonPublic);
        if (mockedTypeProperty is null)
            return null;

        // Return the mocked type
        return mockedTypeProperty.GetValue(mock) as Type;
    }
}
