using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;

namespace Trax.Effect.Decisions.SystemOne;

/// <summary>
/// The decision model could not answer. Its failure class says whether trying again later can help.
/// </summary>
public sealed class DecisionServiceException(string message) : Exception(message);
