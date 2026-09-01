using CaroNet.Storage.Matches;

namespace CaroNet.Storage.Tests;

public sealed class InMemoryMatchHistoryStoreTests
{
    [Fact]
    public async Task SaveMatchAsync_stores_completed_match_with_moves()
    {
        var store = new InMemoryMatchHistoryStore();
        MatchRecord match = CreateCompletedMatch("ROOM-1", DateTime.UtcNow.AddMinutes(-5));

        await store.SaveMatchAsync(match);

        MatchRecord? loaded = await store.GetMatchAsync(match.MatchId);

        Assert.NotNull(loaded);
        Assert.Equal("ROOM-1", loaded!.RoomId);
        Assert.Equal("Alice", loaded.PlayerXName);
        Assert.Equal("Bob", loaded.PlayerOName);
        Assert.Equal("Alice", loaded.WinnerName);
        Assert.Equal(2, loaded.Moves.Count);
        Assert.Equal(7, loaded.Moves[0].Row);
        Assert.Equal(8, loaded.Moves[1].Column);
    }

    [Fact]
    public async Task SaveMatchAsync_rejects_match_that_has_not_ended()
    {
        var store = new InMemoryMatchHistoryStore();
        MatchRecord match = CreateCompletedMatch("ROOM-1", DateTime.UtcNow)
            with
            {
                EndedAtUtc = null
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveMatchAsync(match));
    }

    [Fact]
    public async Task GetMatchAsync_returns_snapshot_not_mutable_store_reference()
    {
        var store = new InMemoryMatchHistoryStore();
        DateTime now = DateTime.UtcNow;
        var moves = new List<MatchMoveRecord>
        {
            new(1, "Alice", 7, 7, now.AddMinutes(-4)),
            new(2, "Bob", 7, 8, now.AddMinutes(-3))
        };
        var match = new MatchRecord(
            Guid.NewGuid(),
            "ROOM-1",
            "Alice",
            "Bob",
            "Alice",
            now.AddMinutes(-5),
            now,
            moves);

        await store.SaveMatchAsync(match);
        moves.Add(new MatchMoveRecord(3, "Alice", 1, 1, now));

        MatchRecord reloaded = (await store.GetMatchAsync(match.MatchId))!;

        Assert.Equal(2, reloaded.Moves.Count);
    }

    [Fact]
    public async Task GetAllMatchesAsync_returns_newest_finished_matches_first()
    {
        var store = new InMemoryMatchHistoryStore();
        MatchRecord older = CreateCompletedMatch("OLD", new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc));
        MatchRecord newer = CreateCompletedMatch("NEW", new DateTime(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc));

        await store.SaveMatchAsync(older);
        await store.SaveMatchAsync(newer);

        IReadOnlyList<MatchRecord> matches = await store.GetAllMatchesAsync();

        Assert.Collection(
            matches,
            match => Assert.Equal("NEW", match.RoomId),
            match => Assert.Equal("OLD", match.RoomId));
    }

    [Fact]
    public async Task GetMatchesByPlayerAsync_returns_only_matches_for_that_player()
    {
        var store = new InMemoryMatchHistoryStore();
        MatchRecord aliceMatch = CreateCompletedMatch("ALICE", DateTime.UtcNow);
        MatchRecord otherMatch = CreateCompletedMatch("OTHER", DateTime.UtcNow)
            with
            {
                PlayerXName = "Cara",
                PlayerOName = "Duy"
            };

        await store.SaveMatchAsync(aliceMatch);
        await store.SaveMatchAsync(otherMatch);

        IReadOnlyList<MatchRecord> matches = await store.GetMatchesByPlayerAsync("alice");

        Assert.Single(matches);
        Assert.Equal("ALICE", matches[0].RoomId);
    }

    [Fact]
    public async Task GetMatchesByUserIdAsync_returns_only_matches_for_that_user()
    {
        var store = new InMemoryMatchHistoryStore();
        Guid aliceUserId = Guid.NewGuid();
        Guid bobUserId = Guid.NewGuid();
        MatchRecord aliceMatch = CreateCompletedMatch("ALICE", DateTime.UtcNow)
            with
            {
                PlayerXUserId = aliceUserId,
                PlayerOUserId = bobUserId,
                WinnerUserId = aliceUserId
            };
        MatchRecord legacyMatch = CreateCompletedMatch("LEGACY", DateTime.UtcNow);

        await store.SaveMatchAsync(aliceMatch);
        await store.SaveMatchAsync(legacyMatch);

        IReadOnlyList<MatchRecord> matches = await store.GetMatchesByUserIdAsync(aliceUserId);

        Assert.Single(matches);
        Assert.Equal("ALICE", matches[0].RoomId);
    }

    private static MatchRecord CreateCompletedMatch(string roomId, DateTime endedAtUtc)
    {
        return new MatchRecord(
            Guid.NewGuid(),
            roomId,
            "Alice",
            "Bob",
            "Alice",
            endedAtUtc.AddMinutes(-5),
            endedAtUtc,
            [
                new MatchMoveRecord(1, "Alice", 7, 7, endedAtUtc.AddMinutes(-4)),
                new MatchMoveRecord(2, "Bob", 7, 8, endedAtUtc.AddMinutes(-3))
            ]);
    }
}
