namespace CaroNet.Storage.Matches;

public sealed record MatchMoveRecord(
    int MoveNumber,
    string PlayerName,
    int Row,
    int Column,
    DateTime TimestampUtc);
