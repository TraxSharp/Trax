using System.Runtime.InteropServices;
using System.Text;

namespace Trax.Api.Console.Terminal.Machines;

/// <summary>A state as the graph draws it.</summary>
/// <param name="Name">The state's name, written in its box.</param>
/// <param name="Initial">Drawn with the entry arrow.</param>
/// <param name="Final">Drawn with rounded corners.</param>
/// <param name="Note">What the legend says of it (invokes a train, spawns a machine), or null.</param>
internal sealed record GraphState(string Name, bool Initial, bool Final, string? Note = null);
