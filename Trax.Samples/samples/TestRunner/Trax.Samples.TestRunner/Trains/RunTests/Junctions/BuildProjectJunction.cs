using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.TestRunner.Trains.RunTests.Junctions;

public class BuildProjectJunction(ILogger<BuildProjectJunction> logger) : Junction<TestRun, TestRun>
{
    public override async Task<TestRun> Run(TestRun input)
    {
        var name = input.Project.Name;
        if (!input.Build)
        {
            logger.LogInformation("Skipping build for {ProjectName}", name);
            return input;
        }

        logger.LogInformation("Building project {ProjectName}", name);

        using var process = new Process();
        process.StartInfo = BuildStartInfo(input.Project.ProjectPath);

        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            logger.LogError("Build failed for {ProjectName}: {Error}", name, stderr);
            throw new InvalidOperationException($"Build failed for {name}:\n{stderr}\n{stdout}");
        }

        logger.LogInformation("Build succeeded for {ProjectName}", name);
        return input;
    }

    /// <summary>
    /// <c>dotnet build</c> for one project. The path goes in <see cref="ProcessStartInfo.ArgumentList"/>,
    /// which passes it as a single argument, so no character in it can add a switch.
    /// </summary>
    internal static ProcessStartInfo BuildStartInfo(string projectPath)
    {
        var info = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "build", projectPath, "--no-restore", "-c", "Debug" })
            info.ArgumentList.Add(argument);
        return info;
    }
}
