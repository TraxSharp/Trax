using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Fakes;

/// <summary>A flags enum, which no schema can describe: <c>SchemaFormatTests</c> checks a record holding one is refused.</summary>
[Flags]
public enum StrictFlags
{
    None = 0,
    Bold = 1,
    Italic = 2,
}
