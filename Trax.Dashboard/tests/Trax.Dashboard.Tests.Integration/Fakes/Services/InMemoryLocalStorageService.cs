using Trax.Dashboard.Services.LocalStorage;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>Browser local storage without a browser, for pages rendered under bUnit.</summary>
public sealed class InMemoryLocalStorageService : ILocalStorageService
{
    private readonly Dictionary<string, object?> _store = new();

    public Task<T?> GetAsync<T>(string key) =>
        Task.FromResult(
            _store.TryGetValue(key, out var value) && value is T typed ? typed : default
        );

    public Task SetAsync<T>(string key, T value)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _store.Remove(key);
        return Task.CompletedTask;
    }
}
