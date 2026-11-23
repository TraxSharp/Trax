using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Connection;

/// <summary>What <see cref="ConsoleSession.CheckVersionAsync"/> found.</summary>
internal abstract record VersionCheck
{
    private VersionCheck() { }

    /// <summary>The host runs this tool's release.</summary>
    public sealed record Same(string Version) : VersionCheck;

    /// <summary>The host runs another release.</summary>
    public sealed record Other(string HostVersion) : VersionCheck
    {
        public string Message =>
            $"This host runs Trax .NET {HostVersion}; trax-console is {ConsoleSession.ToolVersion}. The console sends "
            + $"the documents of its own release, which this host does not serve. Install the matching one: "
            + ConsoleSession.InstallLine(HostVersion);
    }

    /// <summary>The host did not say: refused, unreachable, or not serving operations.</summary>
    public sealed record Failed(string? Code, string Message) : VersionCheck;
}
