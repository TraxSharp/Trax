namespace Trax.Dashboard.Services.ThemeState;

public interface IThemeStateService
{
    string Theme { get; }
    bool IsDarkMode { get; }
    Task InitializeAsync();
    Task SetThemeAsync(string theme);
    Task ToggleThemeAsync();
}
