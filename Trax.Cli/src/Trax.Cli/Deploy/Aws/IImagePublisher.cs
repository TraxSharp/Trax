using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Aws;

/// <summary>Builds a service's image and pushes it to a registry.</summary>
internal interface IImagePublisher
{
    Task PublishAsync(string projectPath, string repositoryUri, string tag, RegistryLogin login);
}
