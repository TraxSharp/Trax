using Trax.Dashboard.Services.LocalStorage;
using Radzen;

namespace Trax.Dashboard.Services.ThemeState;

public class ThemeStateService(ILocalStorageService localStorage, ThemeService radzenThemeService)
    : IThemeStateService
{
    private const string DefaultTheme = "material";
    private const string DefaultDarkTheme = "material-dark";

    private string _theme = DefaultTheme;
    private bool _isInitialized;

    public string Theme => _theme;

    // Convention: all Radzen dark themes end with "-dark"
    public bool IsDarkMode => _theme.EndsWith("-dark");

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

    public async Task SetThemeAsync(string theme)
    {
        _theme = theme;
        radzenThemeService.SetTheme(theme);
        await localStorage.SetAsync(StorageKeys.Theme, theme);
    }

    public async Task ToggleThemeAsync()
    {
        var newTheme = IsDarkMode ? DefaultTheme : DefaultDarkTheme;
        await SetThemeAsync(newTheme);
    }
}
