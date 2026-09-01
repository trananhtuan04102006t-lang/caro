namespace CaroNet.Storage.Matches;

public sealed record MatchRecord(
    Guid MatchId,
    string RoomId,
    string PlayerXName,
    string PlayerOName,
    string? WinnerName,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    IReadOnlyList<MatchMoveRecord> Moves)
{
    public Guid? PlayerXUserId { get; init; }

    public Guid? PlayerOUserId { get; init; }

    public Guid? WinnerUserId { get; init; }

    public bool IsCompleted => EndedAtUtc.HasValue;
}
