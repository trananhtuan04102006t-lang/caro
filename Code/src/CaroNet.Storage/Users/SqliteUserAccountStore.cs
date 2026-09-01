using System.Globalization;
using CaroNet.Storage.Database;
using Microsoft.Data.Sqlite;

namespace CaroNet.Storage.Users;

public sealed class SqliteUserAccountStore : IUserAccountStore
{
    private readonly string _connectionString;

    public SqliteUserAccountStore(string databasePath)
    {
        _connectionString = SqliteConnectionFactory.CreateConnectionString(databasePath);
    }

    public async Task<UserAccount> RegisterAsync(
        string username,
        string password,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        string normalizedUsername = NormalizeUsername(username);
        string normalizedDisplayName = NormalizeDisplayName(displayName);
        ValidatePassword(password);

        var account = new UserAccount(
            Guid.NewGuid(),
            normalizedUsername,
            normalizedDisplayName,
            DateTime.UtcNow);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Users
            (
                UserId,
                Username,
                DisplayName,
                PasswordHash,
                CreatedAtUtc
            )
            VALUES
            (
                $userId,
                $username,
                $displayName,
                $passwordHash,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$userId", account.UserId.ToString());
        command.Parameters.AddWithValue("$username", account.Username);
        command.Parameters.AddWithValue("$displayName", account.DisplayName);
        command.Parameters.AddWithValue("$passwordHash", PasswordHasher.Hash(password));
        command.Parameters.AddWithValue("$createdAt", account.CreatedAtUtc.ToString("O"));

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Tên đăng nhập đã tồn tại.", ex);
        }

        return account;
    }

    public async Task<UserAccount?> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        string normalizedUsername = NormalizeUsername(username);
        ValidatePassword(password);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT UserId, Username, DisplayName, PasswordHash, CreatedAtUtc
            FROM Users
            WHERE Username = $username COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$username", normalizedUsername);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        string passwordHash = reader.GetString(3);
        if (!PasswordHasher.Verify(password, passwordHash))
        {
            return null;
        }

        return ReadAccount(reader);
    }

    public async Task<UserAccount?> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT UserId, Username, DisplayName, PasswordHash, CreatedAtUtc
            FROM Users
            WHERE UserId = $userId;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAccount(reader) : null;
    }

    private static UserAccount ReadAccount(SqliteDataReader reader)
    {
        return new UserAccount(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            DateTime.Parse(reader.GetString(4), null, DateTimeStyles.RoundtripKind));
    }

    private static string NormalizeUsername(string username)
    {
        string value = username.Trim();
        if (value.Length is < 3 or > 32)
        {
            throw new ArgumentException("Tên đăng nhập cần từ 3 đến 32 ký tự.", nameof(username));
        }

        return value;
    }

    private static string NormalizeDisplayName(string displayName)
    {
        string value = displayName.Trim();
        if (value.Length is < 1 or > 32)
        {
            throw new ArgumentException("Tên hiển thị cần từ 1 đến 32 ký tự.", nameof(displayName));
        }

        return value;
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            throw new ArgumentException("Mật khẩu cần tối thiểu 4 ký tự.", nameof(password));
        }
    }
}
