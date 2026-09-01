using CaroNet.Storage.Statistics;

namespace CaroNet.Storage.Tests;

public sealed class InMemoryPlayerRecordStoreTests
{
    [Fact]
    public async Task SaveAsync_stores_and_replaces_player_record()
    {
        var store = new InMemoryPlayerRecordStore();

        await store.SaveAsync(new PlayerRecord("Alice", Wins: 2, Losses: 1, Draws: 0));
        await store.SaveAsync(new PlayerRecord("Alice", Wins: 3, Losses: 1, Draws: 1));

        PlayerRecord? loaded = await store.GetAsync("Alice");

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Wins);
        Assert.Equal(5, loaded.TotalGames);
    }

    [Fact]
    public async Task GetTopPlayersAsync_orders_by_win_rate_then_wins()
    {
        var store = new InMemoryPlayerRecordStore();
        await store.SaveAsync(new PlayerRecord("Alice", Wins: 8, Losses: 2, Draws: 0));
        await store.SaveAsync(new PlayerRecord("Bob", Wins: 4, Losses: 1, Draws: 0));
        await store.SaveAsync(new PlayerRecord("Cara", Wins: 7, Losses: 0, Draws: 3));

        IReadOnlyList<PlayerRecord> topPlayers = await store.GetTopPlayersAsync(2);

        Assert.Collection(
            topPlayers,
            player => Assert.Equal("Alice", player.PlayerName),
            player => Assert.Equal("Bob", player.PlayerName));
    }

    [Fact]
    public async Task SaveAsync_rejects_record_with_empty_player_name()
    {
        var store = new InMemoryPlayerRecordStore();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(new PlayerRecord(" ", Wins: 1, Losses: 0, Draws: 0)));
    }
}
