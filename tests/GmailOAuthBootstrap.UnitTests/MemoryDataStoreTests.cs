using JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap.UnitTests;

public class MemoryDataStoreTests
{
    [Fact]
    public async Task StoreAsync_WhenValueIsProvided_CanRetrieveValue()
    {
        // Arrange
        var store = new MemoryDataStore();

        // Act
        await store.StoreAsync(
            "key",
            "value");
        var result = await store.GetAsync<string>("key");

        // Assert
        Assert.Equal(
            "value",
            result);
    }

    [Fact]
    public async Task DeleteAsync_WhenValueExists_RemovesValue()
    {
        // Arrange
        var store = new MemoryDataStore();
        await store.StoreAsync(
            "key",
            "value");

        // Act
        await store.DeleteAsync<string>("key");

        // Assert
        Assert.Null(await store.GetAsync<string>("key"));
    }

    [Fact]
    public async Task ClearAsync_WhenValuesExist_RemovesEveryValue()
    {
        // Arrange
        var store = new MemoryDataStore();
        await store.StoreAsync(
            "first",
            "value");
        await store.StoreAsync(
            "second",
            "value");

        // Act
        await store.ClearAsync();

        // Assert
        Assert.Null(await store.GetAsync<string>("first"));
        Assert.Null(await store.GetAsync<string>("second"));
    }

    [Fact]
    public async Task GetAsync_WhenValueDoesNotExist_ReturnsNull()
    {
        // Arrange
        var store = new MemoryDataStore();

        // Act
        var result = await store.GetAsync<string>("missing");

        // Assert
        Assert.Null(result);
    }
}
