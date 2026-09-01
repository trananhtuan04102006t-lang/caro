namespace CaroNet.Client.WinUI.ViewModels;

public class BestRecordItem
{
    public int Rank { get; set; }

    public string PlayerName { get; set; } = "";

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Draws { get; set; }

    public int TotalGames => Wins + Losses + Draws;

    public string TotalGamesText => $"{TotalGames} trận";

    public string RecordText => $"{Wins}T - {Losses}B - {Draws}H";

    public string WinRateText => TotalGames == 0
        ? "0%"
        : $"{(double)Wins / TotalGames:P0}";
}
