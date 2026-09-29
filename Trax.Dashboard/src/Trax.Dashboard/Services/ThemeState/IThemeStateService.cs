namespace Trax.Dashboard.Services.ThemeState;

/// <summary>
/// Tracks the dashboard's Radzen theme for one circuit and remembers it in the browser's
/// <c>localStorage</c>. Infrastructure used by the dashboard layout; not intended to be
/// called directly.
/// </summary>
internal interface IThemeStateService
{
    /// <summary>
    /// The current Radzen theme name. <c>material</c> until <see cref="InitializeAsync"/> loads a
    /// stored choice.
    /// </summary>
    string Theme { get; }

    /// <summary>
    /// <see langword="true"/> when <see cref="Theme"/> ends with <c>-dark</c>, the naming
    /// convention every Radzen dark theme follows.
    /// </summary>
    bool IsDarkMode { get; }

    /// <summary>
    /// Loads the stored theme, if any, and applies it to Radzen. Only the first call per
    /// instance does anything. It uses JS interop, so call it after the first render.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Applies <paramref name="theme"/> to Radzen and persists it. The name is not validated.
    /// </summary>
    /// <param name="theme">A Radzen theme name, for example <c>material-dark</c>.</param>
    Task SetThemeAsync(string theme);

    /// <summary>
    /// Switches between <c>material</c> and <c>material-dark</c>. From any dark theme this goes to
    /// <c>material</c>; from any other theme it goes to <c>material-dark</c>.
    /// </summary>
    Task ToggleThemeAsync();
}
