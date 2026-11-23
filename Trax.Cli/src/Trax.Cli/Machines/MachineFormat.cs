using System.Text;
using System.Text.RegularExpressions;
using Trax.StateMachine.Persistence;

namespace Trax.Cli.Machines;

/// <summary>How <c>trax machine show</c> draws a machine.</summary>
internal enum MachineFormat
{
    Text,
    Mermaid,
    Dot,
}
