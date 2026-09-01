namespace CaroNet.Client.WinUI.Models;

public sealed class PlayerRecordSummary
{
    public string PlayerName { get; init; } = string.Empty;

    public int Wins { get; init; }

    public int Losses { get; init; }

    public int Draws { get; init; }

    public int TotalGames => Wins + Losses + Draws;

    public double WinRate => TotalGames == 0
        ? 0
        : (double)Wins / TotalGames;
}
