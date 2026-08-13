using System.Collections.Concurrent;
using Google.Apis.Util.Store;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

internal sealed class MemoryDataStore : IDataStore
{
    private readonly ConcurrentDictionary<string, object> values = new(StringComparer.Ordinal);

    public Task ClearAsync()
    {
        values.Clear();
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        values.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        return Task.FromResult(values.TryGetValue(key, out object? value) ? (T)value : default);
    }

    public Task StoreAsync<T>(string key, T value)
    {
        values[key] = value!;
        return Task.CompletedTask;
    }
}
