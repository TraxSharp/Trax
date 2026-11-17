using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Trax.Effect.Configuration.ClientExport;

/// <summary>
/// The export flag of one command line, read when first asked, so a bad argument is reported where it is used and not
/// as a type initializer's failure.
/// </summary>
internal sealed class ClientExportFlag(Func<IReadOnlyList<string>> args)
{
    private readonly Lazy<string?> _path = new(() => TraxClientExport.PathIn(args()));

    public string? Path => _path.Value;

    public bool Requested => Path is not null;
}
