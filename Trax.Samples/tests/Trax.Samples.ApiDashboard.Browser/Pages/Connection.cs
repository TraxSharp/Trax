using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>What signing in started: the first GraphQL request, and the acknowledged socket's URL.</summary>
public sealed record Connection(Task<IRequest> FirstRequest, Task<string> Acknowledged);
