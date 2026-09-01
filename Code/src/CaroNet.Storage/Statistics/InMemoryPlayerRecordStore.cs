using System.Collections.Concurrent;

namespace CaroNet.Storage.Statistics;

public sealed class InMemoryPlayerRecordStore : IPlayerRecordStore
{
    private readonly ConcurrentDictionary<string, PlayerRecord> _records =
        new(StringComparer.OrdinalIgnoreCase);

    public Task SaveAsync(
        PlayerRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(record);

        _records[record.PlayerName.Trim()] = Normalize(record);
        return Task.CompletedTask;
    }

    public Task<PlayerRecord?> GetAsync(
        string playerName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return Task.FromResult<PlayerRecord?>(null);
        }

        PlayerRecord? record = _records.TryGetValue(playerName.Trim(), out PlayerRecord? stored)
            ? stored with { }
            : null;

        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<PlayerRecord>> GetTopPlayersAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<PlayerRecord>>([]);
        }

        IReadOnlyList<PlayerRecord> records = _records.Values
            .OrderByDescending(record => record.WinRate)
            .ThenByDescending(record => record.Wins)
            .ThenBy(record => record.PlayerName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(record => record with { })
            .ToList();

        return Task.FromResult(records);
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

    private static PlayerRecord Normalize(PlayerRecord record)
    {
        return record with
        {
            PlayerName = record.PlayerName.Trim()
        };
    }
}
