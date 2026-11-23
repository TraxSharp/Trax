using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Fakes;

public enum StrictColor
{
    Red,
    DarkGreen,

    [JsonStringEnumMemberName("sky-blue")]
    SkyBlue,
}
