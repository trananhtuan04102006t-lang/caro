using Microsoft.Data.Sqlite;

namespace CaroNet.Storage.Database;

public sealed class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string databasePath)
    {
        _connectionString = SqliteConnectionFactory.CreateConnectionString(databasePath);
    }

    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);

        connection.Open();

        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            """;
        pragmaCommand.ExecuteNonQuery();

        CreateUsersTable(connection);
        CreateMatchesTable(connection);
        CreateMatchMovesTable(connection);
        CreatePlayerRecordsTable(connection);
        EnsureMatchUserColumns(connection);
    }

    private static void CreateUsersTable(SqliteConnection connection)
    {
        const string sql =
            """
            CREATE TABLE IF NOT EXISTS Users
            (
                UserId TEXT PRIMARY KEY,
                Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                DisplayName TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void CreateMatchesTable(SqliteConnection connection)
    {
        const string sql =
            """
            CREATE TABLE IF NOT EXISTS Matches
            (
                MatchId TEXT PRIMARY KEY,
                RoomId TEXT NOT NULL,
                PlayerXName TEXT NOT NULL,
                PlayerOName TEXT NOT NULL,
                PlayerXUserId TEXT,
                PlayerOUserId TEXT,
                WinnerUserId TEXT,
                WinnerName TEXT,
                StartedAtUtc TEXT NOT NULL,
                EndedAtUtc TEXT NOT NULL
            );
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void EnsureMatchUserColumns(SqliteConnection connection)
    {
        AddColumnIfMissing(connection, "Matches", "PlayerXUserId", "TEXT");
        AddColumnIfMissing(connection, "Matches", "PlayerOUserId", "TEXT");
        AddColumnIfMissing(connection, "Matches", "WinnerUserId", "TEXT");
    }

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnType)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = $"PRAGMA table_info({tableName});";

        using SqliteDataReader reader = checkCommand.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType};";
        alterCommand.ExecuteNonQuery();
    }

    private static void CreateMatchMovesTable(SqliteConnection connection)
    {
        const string sql =
            """
            CREATE TABLE IF NOT EXISTS MatchMoves
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MatchId TEXT NOT NULL,
                MoveNumber INTEGER NOT NULL,
                PlayerName TEXT NOT NULL,
                Row INTEGER NOT NULL,
                Column INTEGER NOT NULL,
                TimestampUtc TEXT NOT NULL,
                UNIQUE (MatchId, MoveNumber),
                FOREIGN KEY (MatchId) REFERENCES Matches(MatchId) ON DELETE CASCADE
            );
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void CreatePlayerRecordsTable(SqliteConnection connection)
    {
        const string sql =
            """
            CREATE TABLE IF NOT EXISTS PlayerRecords
            (
                PlayerName TEXT PRIMARY KEY,
                Wins INTEGER NOT NULL CHECK (Wins >= 0),
                Losses INTEGER NOT NULL CHECK (Losses >= 0),
                Draws INTEGER NOT NULL CHECK (Draws >= 0)
            );
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
