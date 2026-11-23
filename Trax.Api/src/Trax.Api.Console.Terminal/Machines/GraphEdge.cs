using System.Runtime.InteropServices;
using System.Text;

namespace Trax.Api.Console.Terminal.Machines;

/// <summary>A transition as the graph draws it: a line from one box to another, its label written on the line.</summary>
internal sealed record GraphEdge(string From, string To, string Label);
