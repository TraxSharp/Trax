using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// A link to a route the console had before its navigation moved (pasted into a ticket or a log line) still
/// lands: each old path redirects to its new one, its ids carried over and its query kept.
/// </summary>
[TestFixture]
public class MovedRoutesBrowserTests : ConsoleTest
{
    [Test]
    [Covers("route:/executions")]
    [Covers("route:/executions/:id")]
    [Covers("route:/work-queue")]
    [Covers("route:/work-queue/:id")]
    [Covers("route:/dead-letters")]
    [Covers("route:/dead-letters/:id")]
    [Covers("route:/state-machines")]
    [Covers("route:/state-machines/:machine/:owner/:id")]
    [Covers("route:/manifests")]
    [Covers("route:/manifests/:id")]
    [Covers("route:/groups")]
    [Covers("route:/groups/:id")]
    [Covers("route:/cluster")]
    [Covers("route:/realtime")]
    [Covers("route:/logs")]
    [Covers("route:/settings/server")]
    [Covers("route:/settings/effects")]
    [Covers("route:/settings/user")]
    [Covers("route:/persisted-operations")]
    [Covers("route:/persisted-operations/:id")]
    [TestCase("executions?state=FAILED", "runs?state=FAILED")]
    [TestCase("executions/{run}", "runs/{run}")]
    [TestCase("work-queue", "runs/queued")]
    [TestCase("work-queue/{entry}", "runs/queued/{entry}")]
    [TestCase("dead-letters", "runs/dead-letters")]
    [TestCase("dead-letters/{deadLetter}", "runs/dead-letters/{deadLetter}")]
    [TestCase("state-machines", "machines")]
    [TestCase("state-machines/browser-gate/system/{gate}", "machines/browser-gate/system/{gate}")]
    [TestCase("manifests", "schedules")]
    [TestCase("manifests/{manifest}", "schedules/{manifest}")]
    [TestCase("groups", "schedules/groups")]
    [TestCase("groups/{group}", "schedules/groups/{group}")]
    [TestCase("cluster", "system/hosts")]
    [TestCase("realtime", "system/live")]
    [TestCase("logs?level=ERROR", "system/logs?level=ERROR")]
    [TestCase("settings/server", "system/scheduler")]
    [TestCase("settings/effects", "system/effects")]
    [TestCase("settings/user", "preferences")]
    [TestCase("persisted-operations", "api-admin/persisted-operations")]
    [TestCase("persisted-operations/{op}", "api-admin/persisted-operations/{op}")]
    public async Task An_old_route_redirects_to_where_it_lives_now(string from, string to)
    {
        var ids = new Dictionary<string, string>();
        foreach (
            var token in new[] { "run", "entry", "deadLetter", "gate", "manifest", "group", "op" }
        )
            if (from.Contains($"{{{token}}}"))
                ids[token] = await IdAsync(token);
        string Fill(string route) =>
            ids.Aggregate(route, (r, id) => r.Replace($"{{{id.Key}}}", id.Value));

        await Console.OpenAsync(Fill(from), Caller.Operator);

        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(Console.Url(Fill(to)), new() { Timeout = BrowserTimeouts.Render.Ms() });
    }

    private async Task<string> IdAsync(string token) =>
        token switch
        {
            "run" => $"{await Host.QueryAsync(dc => dc.Metadatas.MaxAsync(m => m.Id))}",
            "entry" => $"{await Host.QueryAsync(dc => dc.WorkQueues.MaxAsync(w => w.Id))}",
            "deadLetter" => $"{(await Seed.DeadLetterAsync()).Id}",
            "gate" => $"{(await Seed.ClosedGateAsync()).Instance.Id}",
            "manifest" => $"{(await Seed.OnDemandManifestAsync()).Id}",
            "group" => $"{(await Seed.GroupOfOneAsync()).Group.Id}",
            "op" => (await Seed.PersistedOperationAsync()).Id,
            _ => throw new ArgumentOutOfRangeException(nameof(token), token, null),
        };
}
