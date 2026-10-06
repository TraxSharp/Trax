namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// A workflow job that reads a file out of the repository has to check the repository out first.
///
/// <para>Not ADR-enforcing: it checks how GitHub Actions works, that a job without a checkout has no repository files, rather than a choice between alternatives; the choice it works around, keeping the publish job free of a checkout because it holds the NuGet credential, is the workflow's own and is stated in the remarks.</para>
/// </summary>
/// <remarks>
/// The publish job in <c>nuget_release.yml</c> deliberately has no <c>actions/checkout</c>: it
/// downloads the packed artifact and pushes it, and holds the only credential that can publish to
/// NuGet, so keeping repository code off that runner is the point. It still pointed
/// <c>actions/setup-dotnet</c> at <c>global-json-file: global.json</c>, a file that is therefore
/// never on disk. The step failed, the push never ran, and Trax.Samples.Templates sat at 1.29.1
/// while v1.30.0 through v1.33.1 were tagged and released on GitHub. Nothing was red on the
/// PR, because the failure is downstream of the merge.
/// </remarks>
[TestFixture]
public class WorkflowJobFileAccessTests
{
    /// <summary>
    /// Inputs that name a path inside the repository. Add to this as new ones are used.
    /// </summary>
    private static readonly string[] RepoPathInputs = ["global-json-file:", "project-file:"];

    private static IEnumerable<string> WorkflowFiles() =>
        Directory
            .EnumerateFiles(RepoRoot.Combine(".github", "workflows"), "*.yml")
            .OrderBy(p => p, StringComparer.Ordinal);

    [TestCaseSource(nameof(WorkflowFiles))]
    public void EveryJobReadingARepoFile_ChecksTheRepoOut(string workflowPath)
    {
        var offenders = Jobs(File.ReadAllLines(workflowPath))
            .Where(job =>
                job.Lines.Any(line =>
                    RepoPathInputs.Any(input =>
                        line.Replace(" ", string.Empty).Contains(input, StringComparison.Ordinal)
                    )
                )
            )
            .Where(job =>
                !job.Lines.Any(line => line.Contains("actions/checkout", StringComparison.Ordinal))
            )
            .Select(job => job.Name)
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                $"every job in {RepoRoot.Relative(workflowPath)} that points a step at a file in "
                    + "the repository must run actions/checkout first, or the step reads a path "
                    + "that is not on the runner. A job that must not check the repository out "
                    + "(the publish job holds the NuGet credential) should pin the value inline "
                    + "instead, e.g. dotnet-version: \"10.0.x\"."
            );
    }

    /// <summary>
    /// Splits a workflow into its jobs by indentation: a job header is a key at two spaces under
    /// the top-level <c>jobs:</c>, and the job runs until the next one.
    /// </summary>
    private static List<(string Name, List<string> Lines)> Jobs(string[] lines)
    {
        var jobs = new List<(string Name, List<string> Lines)>();
        var inJobs = false;

        foreach (var line in lines)
        {
            if (!inJobs)
            {
                if (line.StartsWith("jobs:", StringComparison.Ordinal))
                    inJobs = true;
                continue;
            }

            // A non-indented, non-blank, non-comment line ends the jobs block.
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
                break;

            var isJobHeader =
                line.StartsWith("  ", StringComparison.Ordinal)
                && line.Length > 2
                && !char.IsWhiteSpace(line[2])
                && line.TrimEnd().EndsWith(":", StringComparison.Ordinal);

            if (isJobHeader)
                jobs.Add((line.Trim().TrimEnd(':'), []));
            else if (jobs.Count > 0)
                jobs[^1].Lines.Add(line);
        }

        return jobs;
    }
}
