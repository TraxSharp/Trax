using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;

namespace Trax.Samples.ApiDashboard.Browser.Fixtures;

/// <summary>One GraphQL operation the page sent: by <see cref="Id"/>, or as <see cref="Text"/>.</summary>
public sealed record GraphQLSent(
    string Transport,
    string? OperationName,
    string? Id,
    string? Text,
    string Raw
);
