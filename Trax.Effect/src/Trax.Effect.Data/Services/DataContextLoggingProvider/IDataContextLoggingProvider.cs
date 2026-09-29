using Microsoft.Extensions.Logging;
using Trax.Effect.Services.EffectProviderFactory;

namespace Trax.Effect.Data.Services.DataContextLoggingProvider;

/// <summary>
/// Marker for the logger provider that stores log entries through the data context. Nothing
/// resolves it: <c>AddDataContextLogging</c> registers the implementation as an
/// <see cref="ILoggerProvider"/>. Not intended for direct use.
/// </summary>
internal interface IDataContextLoggingProvider : ILoggerProvider { }
