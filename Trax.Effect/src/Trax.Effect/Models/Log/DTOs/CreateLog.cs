using Microsoft.Extensions.Logging;

namespace Trax.Effect.Models.Log.DTOs;

/// <summary>
/// One <c>ILogger</c> call, as <see cref="Log.Create"/> takes it. Infrastructure used by the
/// data-context logger (<c>AddDataContextLogging</c>); not intended to be used directly.
/// </summary>
public class CreateLog
{
    /// <summary>The level the message was logged at.</summary>
    public required LogLevel Level { get; set; }

    /// <summary>
    /// The formatted message. <see cref="Log.Create"/> strips NUL characters and truncates it to
    /// 4000 UTF-16 units.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// The logger category, normally the FullName of the type the <c>ILogger&lt;T&gt;</c> was
    /// created for. Truncated to 500 UTF-16 units and stored as <c>Log.Category</c>.
    /// </summary>
    public required string CategoryName { get; set; }

    /// <summary>The numeric <c>EventId.Id</c> passed to the logging call; 0 when none was given.</summary>
    public required int EventId { get; set; }

    /// <summary>
    /// The exception passed to the logging call, or null. Only its message (truncated to 2000) and
    /// stack trace (truncated to 4000) are stored; its type and inner exceptions are not.
    /// </summary>
    public Exception? Exception { get; set; }
}
