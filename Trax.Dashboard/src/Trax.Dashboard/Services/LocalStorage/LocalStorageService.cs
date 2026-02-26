using System.Text.Json;
using Microsoft.JSInterop;

namespace Trax.Dashboard.Services.LocalStorage;

public class LocalStorageService(IJSRuntime jsRuntime) : ILocalStorageService
{
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
