namespace CaroNet.Storage.Statistics;

public sealed record PlayerRecord(
    string PlayerName,
    int Wins,
    int Losses,
    int Draws)
{
    public int TotalGames => Wins + Losses + Draws;

    public double WinRate => TotalGames == 0
        ? 0
        : (double)Wins / TotalGames;
}
