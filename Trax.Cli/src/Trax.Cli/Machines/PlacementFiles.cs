using System.Globalization;

namespace Trax.Cli.Machines;

/// <summary>The file operations <c>generate</c> places artifacts with; a seam so a failure part-way can be tested.</summary>
internal class PlacementFiles
{
    public virtual void Copy(string source, string destination) =>
        File.Copy(source, destination, overwrite: true);

    public virtual void Move(string source, string destination) => File.Move(source, destination);

    public virtual void Delete(string path) => File.Delete(path);

    public virtual void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <summary>Removes <paramref name="path"/> only when it is empty.</summary>
    public virtual void DeleteEmptyDirectory(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            Directory.Delete(path);
    }
}
