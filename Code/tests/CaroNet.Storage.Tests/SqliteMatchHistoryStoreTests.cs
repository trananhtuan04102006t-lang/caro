using CaroNet.Storage.Database;
using CaroNet.Storage.Matches;

namespace CaroNet.Storage.Tests;

public sealed class SqliteMatchHistoryStoreTests
{
    [Fact]
    public async Task SaveMatchAsync_luu_va_doc_lai_tran_da_ket_thuc_tu_database_moi()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqliteMatchHistoryStore(database.Path);
        MatchRecord match = CreateCompletedMatch(Guid.NewGuid(), "ROOM-1", "Alice", "Bob", "Alice");

        await store.SaveMatchAsync(match);

        var reloadedStore = new SqliteMatchHistoryStore(database.Path);
        MatchRecord? loaded = await reloadedStore.GetMatchAsync(match.MatchId);

        Assert.NotNull(loaded);
        Assert.Equal(match.RoomId, loaded!.RoomId);
        Assert.Equal(match.PlayerXName, loaded.PlayerXName);
        Assert.Equal(match.PlayerOName, loaded.PlayerOName);
        Assert.Equal(match.WinnerName, loaded.WinnerName);
        Assert.Equal(2, loaded.Moves.Count);
    }

    [Fact]
    public async Task SaveMatchAsync_luu_lai_cung_match_khong_nhan_doi_nuoc_di()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqliteMatchHistoryStore(database.Path);
        Guid matchId = Guid.NewGuid();
        MatchRecord first = CreateCompletedMatch(matchId, "ROOM-1", "Alice", "Bob", "Alice");
        MatchRecord replacement = first with
        {
            Moves =
            [
                new MatchMoveRecord(1, "Alice", 3, 3, DateTime.UtcNow)
            ]
        };

        await store.SaveMatchAsync(first);
        await store.SaveMatchAsync(replacement);

        MatchRecord? loaded = await store.GetMatchAsync(matchId);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Moves);
        Assert.Equal(3, loaded.Moves[0].Row);
    }

    [Fact]
    public async Task SaveMatchAsync_tu_choi_tran_chua_ket_thuc()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqliteMatchHistoryStore(database.Path);
        MatchRecord unfinished = CreateCompletedMatch(Guid.NewGuid(), "ROOM-2", "Alice", "Bob", null)
            with
            {
                EndedAtUtc = null
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveMatchAsync(unfinished));
    }

    [Fact]
    public async Task GetMatchesByPlayerAsync_loc_theo_ten_khong_phan_biet_hoa_thuong()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqliteMatchHistoryStore(database.Path);
        MatchRecord aliceMatch = CreateCompletedMatch(Guid.NewGuid(), "ROOM-A", "Alice", "Bob", "Alice");
        MatchRecord otherMatch = CreateCompletedMatch(Guid.NewGuid(), "ROOM-B", "Carol", "Dan", "Carol");

        await store.SaveMatchAsync(aliceMatch);
        await store.SaveMatchAsync(otherMatch);

        IReadOnlyList<MatchRecord> matches = await store.GetMatchesByPlayerAsync("alice");

        Assert.Single(matches);
        Assert.Equal(aliceMatch.MatchId, matches[0].MatchId);
    }

    [Fact]
    public async Task GetMatchesByUserIdAsync_chi_tra_tran_co_user_id_hop_le()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqliteMatchHistoryStore(database.Path);
        Guid aliceUserId = Guid.NewGuid();
        Guid bobUserId = Guid.NewGuid();

        MatchRecord aliceMatch = CreateCompletedMatch(Guid.NewGuid(), "ROOM-A", "Alice", "Bob", "Alice")
            with
            {
                PlayerXUserId = aliceUserId,
                PlayerOUserId = bobUserId,
                WinnerUserId = aliceUserId
            };
        MatchRecord oldMatchWithoutUserId = CreateCompletedMatch(Guid.NewGuid(), "ROOM-OLD", "Alice", "Carol", "Alice");

        await store.SaveMatchAsync(aliceMatch);
        await store.SaveMatchAsync(oldMatchWithoutUserId);

        IReadOnlyList<MatchRecord> matches = await store.GetMatchesByUserIdAsync(aliceUserId);

        Assert.Single(matches);
        Assert.Equal(aliceMatch.MatchId, matches[0].MatchId);
        Assert.Equal(aliceUserId, matches[0].PlayerXUserId);
        Assert.Equal(aliceUserId, matches[0].WinnerUserId);
    }

    private static MatchRecord CreateCompletedMatch(
        Guid matchId,
        string roomId,
        string playerXName,
        string playerOName,
        string? winnerName)
    {
        DateTime startedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        DateTime endedAtUtc = DateTime.UtcNow;

        return new MatchRecord(
            matchId,
            roomId,
            playerXName,
            playerOName,
            winnerName,
            startedAtUtc,
            endedAtUtc,
            [
                new MatchMoveRecord(1, playerXName, 7, 7, startedAtUtc.AddSeconds(10)),
                new MatchMoveRecord(2, playerOName, 7, 8, startedAtUtc.AddSeconds(20))
            ]);
    }
}
