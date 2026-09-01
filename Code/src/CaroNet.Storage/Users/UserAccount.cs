namespace CaroNet.Storage.Users;

public sealed record UserAccount(
    Guid UserId,
    string Username,
    string DisplayName,
    DateTime CreatedAtUtc);
