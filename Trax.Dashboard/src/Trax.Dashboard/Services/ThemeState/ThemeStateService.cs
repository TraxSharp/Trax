using Radzen;
using Trax.Dashboard.Services.LocalStorage;

namespace Trax.Dashboard.Services.ThemeState;

/// <summary>
/// Default <see cref="IThemeStateService"/>, applying the theme through Radzen's
/// <see cref="ThemeService"/> and storing it under <see cref="StorageKeys.Theme"/>.
/// Infrastructure registered by <c>AddTraxDashboard</c>; not intended to be used directly.
/// </summary>
/// <param name="localStorage">Browser storage the theme is read from and written to.</param>
/// <param name="radzenThemeService">Radzen's theme service, which re-renders with the new theme.</param>
internal class ThemeStateService(ILocalStorageService localStorage, ThemeService radzenThemeService)
    : IThemeStateService
{
    private const string DefaultTheme = "material";
    private const string DefaultDarkTheme = "material-dark";

    private string _theme = DefaultTheme;
    private bool _isInitialized;

    /// <inheritdoc/>
    public string Theme => _theme;

    /// <inheritdoc/>
    public bool IsDarkMode => _theme.EndsWith("-dark");

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        var storedTheme = await localStorage.GetAsync<string>(StorageKeys.Theme);
        if (!string.IsNullOrEmpty(storedTheme))
        {
            _theme = storedTheme;
        }

        radzenThemeService.SetTheme(_theme);
        _isInitialized = true;
    }

    /// <inheritdoc/>
    public async Task SetThemeAsync(string theme)
    {
        _theme = theme;
        radzenThemeService.SetTheme(theme);
        await localStorage.SetAsync(StorageKeys.Theme, theme);
    }

    /// <inheritdoc/>
    public async Task ToggleThemeAsync()
    {
        var newTheme = IsDarkMode ? DefaultTheme : DefaultDarkTheme;
        await SetThemeAsync(newTheme);
    }
}
