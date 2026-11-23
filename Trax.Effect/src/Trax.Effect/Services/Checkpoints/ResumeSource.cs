namespace Trax.Effect.Services.Checkpoints;

/// <summary>The run a resume names, as the resumed run checks it before restoring anything.</summary>
/// <param name="Name">Its train's name.</param>
/// <param name="Input">Its stored input, or null.</param>
/// <param name="State">How it ended, or that it has not.</param>
/// <param name="ResumedToCompletion">True when a run that resumed it already completed.</param>
internal sealed record ResumeSource(
    string Name,
    string? Input,
    Enums.TrainState State,
    bool ResumedToCompletion
);
