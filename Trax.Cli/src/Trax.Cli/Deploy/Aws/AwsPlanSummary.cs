using System.Globalization;
using System.Text.Json.Nodes;

namespace Trax.Cli.Deploy.Aws;

/// <summary>What <c>trax deploy plan</c> says about a template: what it creates, and roughly what it costs idle.</summary>
internal static class AwsPlanSummary
{
    // Approximate monthly prices, us-east-1 on-demand, 730 hours, October 2026. Other regions differ.
    private const decimal NatGateway = 32.85m;
    private const decimal PublicAddress = 3.65m;
    private const decimal LoadBalancer = 16.43m;
    private const decimal Task = 18.02m; // 0.5 vCPU, 1 GB, x86
    private const decimal SmallMultiAzDatabase = 51.32m; // db.t4g.small Multi-AZ, 20 GB gp3
    private const decimal MicroDatabase = 14.00m; // db.t4g.micro single-AZ, 20 GB gp3

    public static IEnumerable<string> Lines(
        DeployFile file,
        string environment,
        JsonObject template
    )
    {
        var env = file.Environment(environment);
        var resources = (JsonObject)template["Resources"]!;
        yield return $"{file.StackName(environment)} ({env.Profile}, {env.Region}) has {resources.Count} resources:";
        foreach (
            var group in resources
                .Select(r => (string)r.Value!["Type"]!)
                .GroupBy(t => t)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
            yield return $"  {group.Count(), 3}  {group.Key}";

        var tasks = file.Services.Keys.Sum(s => file.Count(env, s));
        var costs = new List<(string What, decimal Monthly)>();
        if (resources.ContainsKey("NatGateway"))
            costs.Add(("NAT gateway and its address", NatGateway + PublicAddress));
        if (resources.ContainsKey("LoadBalancer"))
            costs.Add(("Load balancer", LoadBalancer));
        if (resources.ContainsKey("Database"))
            costs.Add(
                env.Profile == DeployEnvironment.Production
                    ? ("Database, db.t4g.small Multi-AZ", SmallMultiAzDatabase)
                    : ("Database, db.t4g.micro", MicroDatabase)
            );
        costs.Add(($"{tasks} Fargate task(s), 0.5 vCPU and 1 GB each", tasks * Task));
        if (env.Profile == DeployEnvironment.Try && env.Network is null)
            costs.Add(($"{tasks} public task address(es)", tasks * PublicAddress));

        yield return "Idle cost, roughly (us-east-1 on-demand prices; traffic, storage growth and CloudFront extra):";
        foreach (var (what, monthly) in costs)
            yield return $"  {Money(monthly), 9}  {what}";
        yield return $"  {Money(costs.Sum(c => c.Monthly)), 9}  a month";
    }

    private static string Money(decimal amount) =>
        "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);
}
