using System.Collections.Concurrent;

namespace CaroNet.Storage.Matches;

public sealed class InMemoryMatchHistoryStore : IMatchHistoryStore
{
    private readonly ConcurrentDictionary<Guid, MatchRecord> _matches = new();

    public Task SaveMatchAsync(
        MatchRecord match,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCompletedMatch(match);

        _matches[match.MatchId] = Clone(match);
        return Task.CompletedTask;
    }

    public Task<MatchRecord?> GetMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MatchRecord? match = _matches.TryGetValue(matchId, out MatchRecord? stored)
            ? Clone(stored)
            : null;

        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<MatchRecord>> GetAllMatchesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MatchRecord> matches = _matches.Values
            .OrderByDescending(match => match.EndedAtUtc)
            .ThenBy(match => match.RoomId, StringComparer.OrdinalIgnoreCase)
            .Select(Clone)
            .ToList();

        return Task.FromResult(matches);
    }

    public Task<IReadOnlyList<MatchRecord>> GetMatchesByPlayerAsync(
        string playerName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return Task.FromResult<IReadOnlyList<MatchRecord>>([]);
        }

        IReadOnlyList<MatchRecord> matches = _matches.Values
            .Where(match =>
                string.Equals(match.PlayerXName, playerName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(match.PlayerOName, playerName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(match => match.EndedAtUtc)
            .Select(Clone)
            .ToList();

        return Task.FromResult(matches);
    }

    public Task<IReadOnlyList<MatchRecord>> GetMatchesByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (userId == Guid.Empty)
        {
            return Task.FromResult<IReadOnlyList<MatchRecord>>([]);
        }

        IReadOnlyList<MatchRecord> matches = _matches.Values
            .Where(match => match.PlayerXUserId == userId || match.PlayerOUserId == userId)
            .OrderByDescending(match => match.EndedAtUtc)
            .ThenBy(match => match.RoomId, StringComparer.OrdinalIgnoreCase)
            .Select(Clone)
            .ToList();

        return Task.FromResult(matches);
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

    private static MatchRecord Clone(MatchRecord match)
    {
        // Trả snapshot riêng để caller không sửa trực tiếp dữ liệu đang giữ trong store.
        return match with
        {
            Moves = match.Moves
                .Select(move => move with { })
                .ToList()
        };
    }
}
