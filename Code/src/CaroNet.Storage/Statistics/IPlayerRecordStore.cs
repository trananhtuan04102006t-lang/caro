namespace CaroNet.Storage.Statistics;

public interface IPlayerRecordStore
{
    Task SaveAsync(
        PlayerRecord record,
        CancellationToken cancellationToken = default);

    Task<PlayerRecord?> GetAsync(
        string playerName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerRecord>> GetTopPlayersAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
