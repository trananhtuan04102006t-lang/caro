using Microsoft.Data.Sqlite;
using CaroNet.Storage.Database;

namespace CaroNet.Storage.Statistics;

public sealed class SqlitePlayerRecordStore : IPlayerRecordStore
{
    private readonly string _connectionString;

    public SqlitePlayerRecordStore(string databasePath)
    {
        _connectionString = SqliteConnectionFactory.CreateConnectionString(databasePath);
    }

    public async Task SaveAsync(
        PlayerRecord record,
        CancellationToken cancellationToken = default)
    {
        Validate(record);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR REPLACE INTO PlayerRecords
            (
                PlayerName,
                Wins,
                Losses,
                Draws
            )
            VALUES
            (
                $playerName,
                $wins,
                $losses,
                $draws
            );
            """;

        command.Parameters.AddWithValue("$playerName", record.PlayerName.Trim());
        command.Parameters.AddWithValue("$wins", record.Wins);
        command.Parameters.AddWithValue("$losses", record.Losses);
        command.Parameters.AddWithValue("$draws", record.Draws);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PlayerRecord?> GetAsync(
        string playerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PlayerName, Wins, Losses, Draws
            FROM PlayerRecords
            WHERE PlayerName = $playerName COLLATE NOCASE;
            """;

        command.Parameters.AddWithValue("$playerName", playerName.Trim());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PlayerRecord(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3));
    }

    public async Task<IReadOnlyList<PlayerRecord>> GetTopPlayersAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PlayerName, Wins, Losses, Draws
            FROM PlayerRecords
            ORDER BY
                CASE
                    WHEN Wins + Losses + Draws = 0 THEN 0.0
                    ELSE CAST(Wins AS REAL) / CAST(Wins + Losses + Draws AS REAL)
                END DESC,
                Wins DESC,
                PlayerName COLLATE NOCASE ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var records = new List<PlayerRecord>();

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(
                new PlayerRecord(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3)));
        }

        return records;
    }

    private static void Validate(PlayerRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.PlayerName))
        {
            throw new ArgumentException("Tên người chơi không được để trống.", nameof(record));
        }

        if (record.Wins < 0 || record.Losses < 0 || record.Draws < 0)
        {
            throw new ArgumentException("Số trận thắng/thua/hòa không được âm.", nameof(record));
        }
    }
}
