using System.Text.Json;
using Microsoft.JSInterop;

namespace Trax.Dashboard.Services.LocalStorage;

/// <summary>
/// Default <see cref="ILocalStorageService"/>, calling <c>localStorage.getItem</c>,
/// <c>setItem</c> and <c>removeItem</c> through <see cref="IJSRuntime"/> and swallowing every
/// failure. Infrastructure registered by <c>AddTraxDashboard</c>; not intended to be used directly.
/// </summary>
/// <param name="jsRuntime">The circuit's JS runtime.</param>
internal class LocalStorageService(IJSRuntime jsRuntime) : ILocalStorageService
{
    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);

            if (string.IsNullOrEmpty(json))
                return default;

            if (typeof(T) == typeof(string))
                return (T)(object)json;

            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            // localStorage not available (e.g., during prerendering)
            return default;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value)
    {
        try
        {
            var json =
                typeof(T) == typeof(string) ? value?.ToString() : JsonSerializer.Serialize(value);

            await jsRuntime.InvokeVoidAsync("localStorage.setItem", key, json);
        }
        catch
        {
            // localStorage not available
        }
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch
        {
            // localStorage not available
        }
    }
}
