using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Trax.Api.Console.Terminal.Connection;

namespace Trax.Api.Console.Terminal.Code;

/// <summary>Where a type's source is: the file and the line its declaration starts on.</summary>
internal sealed record SourceLocation(string Path, int Line);
