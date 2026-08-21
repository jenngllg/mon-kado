using Google.Apis.Util.Store;

using System.Collections.Concurrent;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

internal class MemoryDataStore : IDataStore
{
    private readonly ConcurrentDictionary<string, object> _values = new(StringComparer.Ordinal);

    public Task ClearAsync()
    {
        _values.Clear();

        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        _values.TryRemove(
            key,
            out _);

        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key)
    {

        return Task.FromResult(_values.TryGetValue(
            key,
            out var value) ? (T)value : default);
    }

    public Task StoreAsync<T>(
        string key,
        T value)
    {
        _values[key] = value!;

        return Task.CompletedTask;
    }
}
