using CaroNet.Storage.Statistics;

namespace CaroNet.Storage.Tests;

public sealed class SqlitePlayerRecordStoreTests
{
    [Fact]
    public async Task SaveAsync_luu_va_doc_lai_best_record_tu_database_moi()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqlitePlayerRecordStore(database.Path);
        var record = new PlayerRecord("Alice", 10, 2, 1);

        await store.SaveAsync(record);

        var reloadedStore = new SqlitePlayerRecordStore(database.Path);
        PlayerRecord? loaded = await reloadedStore.GetAsync("alice");

        Assert.NotNull(loaded);
        Assert.Equal(record.PlayerName, loaded!.PlayerName);
        Assert.Equal(record.Wins, loaded.Wins);
        Assert.Equal(record.Losses, loaded.Losses);
        Assert.Equal(record.Draws, loaded.Draws);
    }

    [Fact]
    public async Task SaveAsync_cap_nhat_record_cung_ten()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqlitePlayerRecordStore(database.Path);

        await store.SaveAsync(new PlayerRecord("Alice", 1, 2, 0));
        await store.SaveAsync(new PlayerRecord("Alice", 5, 1, 1));

        PlayerRecord? loaded = await store.GetAsync("Alice");

        Assert.NotNull(loaded);
        Assert.Equal(5, loaded!.Wins);
        Assert.Equal(1, loaded.Losses);
        Assert.Equal(1, loaded.Draws);
    }

    [Fact]
    public async Task GetTopPlayersAsync_sap_xep_theo_win_rate_roi_so_tran_thang()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqlitePlayerRecordStore(database.Path);

        await store.SaveAsync(new PlayerRecord("Bob", 3, 1, 0));
        await store.SaveAsync(new PlayerRecord("Alice", 4, 0, 0));
        await store.SaveAsync(new PlayerRecord("Carol", 2, 0, 0));

        IReadOnlyList<PlayerRecord> topPlayers = await store.GetTopPlayersAsync(2);

        Assert.Collection(
            topPlayers,
            first => Assert.Equal("Alice", first.PlayerName),
            second => Assert.Equal("Carol", second.PlayerName));
    }

    [Fact]
    public async Task SaveAsync_tu_choi_record_am()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqlitePlayerRecordStore(database.Path);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(new PlayerRecord("Alice", -1, 0, 0)));
    }
}
