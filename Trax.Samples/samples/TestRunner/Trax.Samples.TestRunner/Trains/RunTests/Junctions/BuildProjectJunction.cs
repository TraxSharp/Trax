using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.TestRunner.Trains.RunTests.Junctions;

public class BuildProjectJunction(ILogger<BuildProjectJunction> logger)
    : Junction<RunTestsInput, RunTestsInput>
{
    public override async Task<RunTestsInput> Run(RunTestsInput input)
    {
        if (!input.Build)
        {
            logger.LogInformation("Skipping build for {ProjectName}", input.ProjectName);
            return input;
        }

        logger.LogInformation("Building project {ProjectName}", input.ProjectName);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{input.ProjectPath}\" --no-restore -c Debug",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            logger.LogError("Build failed for {ProjectName}: {Error}", input.ProjectName, stderr);
            throw new InvalidOperationException(
                $"Build failed for {input.ProjectName}:\n{stderr}\n{stdout}"
            );
        }

        logger.LogInformation("Build succeeded for {ProjectName}", input.ProjectName);
        return input;
    }
}
