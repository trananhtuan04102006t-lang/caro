using System.Globalization;
using Microsoft.Data.Sqlite;
using CaroNet.Storage.Database;

namespace CaroNet.Storage.Matches;

public sealed class SqliteMatchHistoryStore : IMatchHistoryStore
{
    private readonly string _connectionString;

    public SqliteMatchHistoryStore(string databasePath)
    {
        _connectionString = SqliteConnectionFactory.CreateConnectionString(databasePath);
    }

    public async Task SaveMatchAsync(
        MatchRecord match,
        CancellationToken cancellationToken = default)
    {
        ValidateCompletedMatch(match);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await DeleteExistingMovesAsync(connection, transaction, match.MatchId, cancellationToken);
        await SaveMatchHeaderAsync(connection, transaction, match, cancellationToken);

        foreach (MatchMoveRecord move in match.Moves)
        {
            await SaveMoveAsync(connection, transaction, match.MatchId, move, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<MatchRecord?> GetMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var matchCommand = connection.CreateCommand();
        matchCommand.CommandText =
            """
            SELECT RoomId, PlayerXName, PlayerOName,
                   PlayerXUserId, PlayerOUserId, WinnerUserId,
                   WinnerName, StartedAtUtc, EndedAtUtc
            FROM Matches
            WHERE MatchId = $matchId;
            """;
        matchCommand.Parameters.AddWithValue("$matchId", matchId.ToString());

        await using var reader = await matchCommand.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        string roomId = reader.GetString(0);
        string playerX = reader.GetString(1);
        string playerO = reader.GetString(2);
        Guid? playerXUserId = ReadNullableGuid(reader, 3);
        Guid? playerOUserId = ReadNullableGuid(reader, 4);
        Guid? winnerUserId = ReadNullableGuid(reader, 5);
        string? winner = reader.IsDBNull(6) ? null : reader.GetString(6);
        DateTime startedAtUtc = ParseUtc(reader.GetString(7));
        DateTime? endedAtUtc = reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8));

        IReadOnlyList<MatchMoveRecord> moves =
            await ReadMovesAsync(connection, matchId, cancellationToken);

        return new MatchRecord(
            matchId,
            roomId,
            playerX,
            playerO,
            winner,
            startedAtUtc,
            endedAtUtc,
            moves)
        {
            PlayerXUserId = playerXUserId,
            PlayerOUserId = playerOUserId,
            WinnerUserId = winnerUserId
        };
    }

    public async Task<IReadOnlyList<MatchRecord>> GetAllMatchesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT MatchId
            FROM Matches
            ORDER BY EndedAtUtc DESC, RoomId COLLATE NOCASE ASC;
            """;

        return await ReadMatchesByIdQueryAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<MatchRecord>> GetMatchesByPlayerAsync(
        string playerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return [];
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT MatchId
            FROM Matches
            WHERE PlayerXName = $playerName COLLATE NOCASE
               OR PlayerOName = $playerName COLLATE NOCASE
            ORDER BY EndedAtUtc DESC, RoomId COLLATE NOCASE ASC;
            """;
        command.Parameters.AddWithValue("$playerName", playerName.Trim());

        return await ReadMatchesByIdQueryAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<MatchRecord>> GetMatchesByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return [];
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT MatchId
            FROM Matches
            WHERE PlayerXUserId = $userId
               OR PlayerOUserId = $userId
            ORDER BY EndedAtUtc DESC, RoomId COLLATE NOCASE ASC;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());

        return await ReadMatchesByIdQueryAsync(command, cancellationToken);
    }

    private async Task<IReadOnlyList<MatchRecord>> ReadMatchesByIdQueryAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var matchIds = new List<Guid>();

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matchIds.Add(Guid.Parse(reader.GetString(0)));
        }

        var matches = new List<MatchRecord>(matchIds.Count);
        foreach (Guid matchId in matchIds)
        {
            MatchRecord? match = await GetMatchAsync(matchId, cancellationToken);
            if (match is not null)
            {
                matches.Add(match);
            }
        }

        return matches;
    }

    private static async Task SaveMatchHeaderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MatchRecord match,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR REPLACE INTO Matches
            (
                MatchId,
                RoomId,
                PlayerXName,
                PlayerOName,
                PlayerXUserId,
                PlayerOUserId,
                WinnerUserId,
                WinnerName,
                StartedAtUtc,
                EndedAtUtc
            )
            VALUES
            (
                $matchId,
                $roomId,
                $playerX,
                $playerO,
                $playerXUserId,
                $playerOUserId,
                $winnerUserId,
                $winner,
                $started,
                $ended
            );
            """;

        command.Parameters.AddWithValue("$matchId", match.MatchId.ToString());
        command.Parameters.AddWithValue("$roomId", match.RoomId.Trim());
        command.Parameters.AddWithValue("$playerX", match.PlayerXName.Trim());
        command.Parameters.AddWithValue("$playerO", match.PlayerOName.Trim());
        command.Parameters.AddWithValue("$playerXUserId", (object?)match.PlayerXUserId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$playerOUserId", (object?)match.PlayerOUserId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$winnerUserId", (object?)match.WinnerUserId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$winner", (object?)match.WinnerName?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$started", match.StartedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$ended", match.EndedAtUtc!.Value.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveMoveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid matchId,
        MatchMoveRecord move,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO MatchMoves
            (
                MatchId,
                MoveNumber,
                PlayerName,
                Row,
                Column,
                TimestampUtc
            )
            VALUES
            (
                $matchId,
                $moveNumber,
                $player,
                $row,
                $column,
                $timestamp
            );
            """;

        command.Parameters.AddWithValue("$matchId", matchId.ToString());
        command.Parameters.AddWithValue("$moveNumber", move.MoveNumber);
        command.Parameters.AddWithValue("$player", move.PlayerName.Trim());
        command.Parameters.AddWithValue("$row", move.Row);
        command.Parameters.AddWithValue("$column", move.Column);
        command.Parameters.AddWithValue("$timestamp", move.TimestampUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteExistingMovesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM MatchMoves
            WHERE MatchId = $matchId;
            """;
        command.Parameters.AddWithValue("$matchId", matchId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<MatchMoveRecord>> ReadMovesAsync(
        SqliteConnection connection,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT MoveNumber, PlayerName, Row, Column, TimestampUtc
            FROM MatchMoves
            WHERE MatchId = $matchId
            ORDER BY MoveNumber ASC;
            """;
        command.Parameters.AddWithValue("$matchId", matchId.ToString());

        var moves = new List<MatchMoveRecord>();

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            moves.Add(
                new MatchMoveRecord(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    ParseUtc(reader.GetString(4))));
        }

        return moves;
    }

    private static DateTime ParseUtc(string value)
    {
        return DateTime.Parse(value, null, DateTimeStyles.RoundtripKind);
    }

    private static Guid? ReadNullableGuid(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Guid.TryParse(reader.GetString(ordinal), out Guid value)
            ? value
            : null;
    }

    private static void ValidateCompletedMatch(MatchRecord match)
    {
        if (match.MatchId == Guid.Empty)
        {
            throw new ArgumentException("MatchId không được để trống.", nameof(match));
        }

        if (string.IsNullOrWhiteSpace(match.RoomId))
        {
            throw new ArgumentException("RoomId không được để trống.", nameof(match));
        }

        if (string.IsNullOrWhiteSpace(match.PlayerXName) ||
            string.IsNullOrWhiteSpace(match.PlayerOName))
        {
            throw new ArgumentException("Tên người chơi không được để trống.", nameof(match));
        }

        if (!match.IsCompleted)
        {
            throw new InvalidOperationException("Chỉ lưu lịch sử khi ván đấu đã kết thúc.");
        }

        if (match.EndedAtUtc < match.StartedAtUtc)
        {
            throw new ArgumentException("Thời điểm kết thúc không được trước thời điểm bắt đầu.", nameof(match));
        }
    }
}
