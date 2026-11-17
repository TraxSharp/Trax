using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

/// <summary>One declared track: its name, what it is for, its body, and its own confidence bar.</summary>
internal sealed record DeclaredTrack<TInput, TReturn>(
    string Name,
    string? Description,
    Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> Body,
    double? RequireConfidence
);
