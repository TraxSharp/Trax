using System.Diagnostics;
using System.Globalization;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy;

/// <summary>Asks a project's host what it needs from its deployment.</summary>
internal interface IHostExporter
{
    DeployManifest Export(string projectPath, IReadOnlyDictionary<string, string> environment);
}
