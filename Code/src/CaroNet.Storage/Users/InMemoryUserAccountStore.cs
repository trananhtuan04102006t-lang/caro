using System.Collections.Concurrent;

namespace CaroNet.Storage.Users;

public sealed class InMemoryUserAccountStore : IUserAccountStore
{
    private readonly ConcurrentDictionary<string, (UserAccount Account, string PasswordHash)> _users =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<UserAccount> RegisterAsync(
        string username,
        string password,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedUsername = ValidateUsername(username);
        string normalizedDisplayName = ValidateDisplayName(displayName);
        ValidatePassword(password);

        var account = new UserAccount(
            Guid.NewGuid(),
            normalizedUsername,
            normalizedDisplayName,
            DateTime.UtcNow);

        bool added = _users.TryAdd(
            normalizedUsername,
            (account, PasswordHasher.Hash(password)));

        if (!added)
        {
            throw new InvalidOperationException("Tên đăng nhập đã tồn tại.");
        }

        return Task.FromResult(account);
    }

    public Task<UserAccount?> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedUsername = ValidateUsername(username);
        ValidatePassword(password);

        if (!_users.TryGetValue(normalizedUsername, out var user) ||
            !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return Task.FromResult<UserAccount?>(null);
        }

        return Task.FromResult<UserAccount?>(user.Account);
    }

    public Task<UserAccount?> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        UserAccount? account = _users.Values
            .Select(user => user.Account)
            .FirstOrDefault(user => user.UserId == userId);

        return Task.FromResult(account);
    }

    private static string ValidateUsername(string username)
    {
        string value = username.Trim();
        if (value.Length is < 3 or > 32)
        {
            throw new ArgumentException("Tên đăng nhập cần từ 3 đến 32 ký tự.", nameof(username));
        }

        return value;
    }

    private static string ValidateDisplayName(string displayName)
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
