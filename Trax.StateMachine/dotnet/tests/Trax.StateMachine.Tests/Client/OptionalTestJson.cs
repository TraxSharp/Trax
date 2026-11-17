using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Trax.Client;

namespace Trax.StateMachine.Tests.Client;

// A source-generated context over the Optional records, as a trimmed or AOT app passes one. Each T an Optional<T> wraps
// is listed too: the context cannot see through Optional's converter.
[JsonSerializable(typeof(TraxClientTests.JobVariables))]
[JsonSerializable(typeof(TraxClientTests.JobResult))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(string))]
internal sealed partial class OptionalTestJson : JsonSerializerContext;
