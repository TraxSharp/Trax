namespace Trax.Dashboard.Services.LocalStorage;

/// <summary>
/// Reads and writes values in the current browser's <c>localStorage</c> through JS interop.
/// Infrastructure used by the dashboard's settings and theme services; not intended to be
/// called directly.
/// </summary>
/// <remarks>
/// Values are stored as JSON, except strings, which are stored as-is. The default
/// implementation never throws: when JS interop is unavailable (during prerendering, or after
/// the circuit disconnects) reads return <see langword="default"/> and writes do nothing.
/// </remarks>
internal interface ILocalStorageService
{
    /// <summary>
    /// Returns the value stored under <paramref name="key"/>, or <see langword="default"/> when
    /// the key is missing or empty, storage is unavailable, or the stored JSON does not
    /// deserialize to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to. Use a nullable type to tell "missing" from a stored default.</typeparam>
    /// <param name="key">The <c>localStorage</c> key, usually one of <see cref="StorageKeys"/>.</param>
    Task<T?> GetAsync<T>(string key);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/>, replacing any previous value.
    /// </summary>
    /// <typeparam name="T">The value's type; strings are stored verbatim, anything else as JSON.</typeparam>
    /// <param name="key">The <c>localStorage</c> key, usually one of <see cref="StorageKeys"/>.</param>
    /// <param name="value">The value to store.</param>
    Task SetAsync<T>(string key, T value);

    /// <summary>
    /// Deletes <paramref name="key"/> from <c>localStorage</c>. Does nothing if it is absent.
    /// </summary>
    /// <param name="key">The <c>localStorage</c> key to remove.</param>
    Task RemoveAsync(string key);
}
