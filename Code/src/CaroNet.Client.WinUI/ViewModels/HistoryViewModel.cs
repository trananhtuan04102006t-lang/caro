using CaroNet.Client.WinUI.Models;
using CaroNet.Client.WinUI.Services;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace CaroNet.Client.WinUI.ViewModels;

public sealed class HistoryViewModel
{
    public ObservableCollection<MatchSummary> Matches { get; } = new();

    public async Task LoadAsync()
    {
        Matches.Clear();

        var matches = await AppServices.GameClient.GetMyHistoryAsync(
            CancellationToken.None);

        foreach (var match in matches)
        {
            Matches.Add(match);
        }
    }
}
