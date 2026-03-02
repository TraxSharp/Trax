using System.Reflection;

namespace Trax.Effect.Extensions;

/// <summary>
/// Provides extension methods for working with Assembly objects.
/// These methods simplify common operations related to assembly identification and metadata.
/// </summary>
/// <remarks>
/// Assembly extensions are particularly useful in the Trax.Effect system
/// for identifying the source of trains and tracking their execution across
/// different assemblies in a modular application.
/// </remarks>
public static class AssemblyExtensions
{
    /// <summary>
    /// Extracts the project name from an assembly.
    /// </summary>
    /// <param name="assembly">The assembly to extract the project name from</param>
    /// <returns>The project name (first part of the assembly's full name)</returns>
    /// <exception cref="NullReferenceException">Thrown if the assembly's full name is null</exception>
    /// <exception cref="Exception">Thrown if the assembly name cannot be extracted from the full name</exception>
    /// <remarks>
    /// This method parses the assembly's full name to extract just the project name portion.
    /// The assembly full name typically follows the format "ProjectName, Version=x.x.x.x, Culture=neutral, PublicKeyToken=null".
    /// This method extracts just the "ProjectName" portion.
    ///
    /// In the Trax.Effect system, this is used to identify the source of trains
    /// in the metadata tracking system, allowing for filtering and analysis by project.
    /// </remarks>
    public static string GetAssemblyProject(this Assembly assembly)
    {
        if (assembly.FullName is null)
            throw new NullReferenceException("Could not get FullName of Entry Assembly");

        var assemblyNameArray = assembly.FullName.Split(",");

        var assemblyName = assemblyNameArray.FirstOrDefault();

        if (assemblyName is null)
            throw new Exception($"Could not get name of Assembly from ({assembly.FullName})");

        return assemblyName;
    }
}
