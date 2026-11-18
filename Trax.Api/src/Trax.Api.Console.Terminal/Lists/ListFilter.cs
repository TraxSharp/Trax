using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>A filter a list takes, by the name its query variable has.</summary>
/// <param name="Name">The filter's name on the command line (<c>--where state=FAILED</c>) and in the filter dialog.</param>
/// <param name="Description">What it filters by, for the filter dialog and --help.</param>
/// <param name="Choices">The values an enum filter takes, as the host writes them; null for free text.</param>
internal sealed record ListFilter(
    string Name,
    string Description,
    IReadOnlyList<string>? Choices = null
);
