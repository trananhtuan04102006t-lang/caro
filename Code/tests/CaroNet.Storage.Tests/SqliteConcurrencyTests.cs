using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CaroNet.Storage.Database;
using CaroNet.Storage.Matches;
using Xunit;

namespace CaroNet.Storage.Tests;

public class SqliteConcurrencyTests
{
    [Fact]
    public async Task SaveMatchAsync_ShouldHandleConcurrentWrites_WithoutLockingDatabase()
    {
        string testDbPath = Path.Combine(
            Path.GetTempPath(),
            "CaroNetTests",
            $"{Guid.NewGuid():N}.db");

        var initializer = new DatabaseInitializer(testDbPath);
        initializer.Initialize();

        var store = new SqliteMatchHistoryStore(testDbPath);

        int numberOfConcurrentMatches = 5;
        var concurrentMatches = new List<MatchRecord>();

        for (int i = 1; i <= numberOfConcurrentMatches; i++)
        {
            var matchId = Guid.NewGuid();
            var moves = new List<MatchMoveRecord>
            {
                new(1, "PlayerX", 7, 7, DateTime.UtcNow.AddSeconds(5)),
                new(2, "PlayerO", 8, 8, DateTime.UtcNow.AddSeconds(15))
            };

            var matchRecord = new MatchRecord(
                matchId,
                $"Room_Test_{i}",
                "PlayerX",
                "PlayerO",
                "PlayerX",
                DateTime.UtcNow.AddMinutes(-10),
                DateTime.UtcNow,
                moves);

            concurrentMatches.Add(matchRecord);
        }

        IEnumerable<Task> saveTasks = concurrentMatches.Select(match =>
            store.SaveMatchAsync(match, CancellationToken.None));

        await Task.WhenAll(saveTasks);

        IReadOnlyList<MatchRecord> savedMatches = await store.GetAllMatchesAsync();
        Assert.Equal(numberOfConcurrentMatches, savedMatches.Count);
    }
}
