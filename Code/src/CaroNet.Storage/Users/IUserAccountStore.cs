namespace CaroNet.Storage.Users;

public interface IUserAccountStore
{
    Task<UserAccount> RegisterAsync(
        string username,
        string password,
        string displayName,
        CancellationToken cancellationToken = default);

    Task<UserAccount?> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task<UserAccount?> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
