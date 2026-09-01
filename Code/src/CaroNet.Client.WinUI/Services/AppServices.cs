using CaroNet.Storage.Matches;
using CaroNet.Storage.Statistics;
using CaroNet.Shared.Game.AI;
using System;
using System.IO;
using System.Threading;

namespace CaroNet.Client.WinUI.Services;

public static class AppServices
{
    // GameClient giữ nguyên service online cũ của project.
    // Chế độ AI được mở bằng ActiveAiGameClient để không phá luồng PvP hiện tại.
    public static IGameClientService GameClient { get; } =
        new SocketGameClientService(new SocketClientConnection());

    public static LocalAiGameClientService? ActiveAiGameClient { get; private set; }

    public static bool IsAiMode => ActiveAiGameClient is not null;

    public static void StartAiGame(AiDifficulty difficulty)
    {
        var currentAuth = GameClient.CurrentAuth;
        string playerName = string.IsNullOrWhiteSpace(currentAuth?.DisplayName)
            ? GameClient.CurrentState.PlayerName
            : currentAuth!.DisplayName;

        ActiveAiGameClient?.Dispose();
        ActiveAiGameClient = new LocalAiGameClientService(
            playerName,
            currentAuth,
            difficulty);

        ActiveAiGameClient.CreateRoomAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public static IGameClientService GetGameClientForCurrentMode() =>
        ActiveAiGameClient ?? GameClient;

    public static void EndAiGame()
    {
        ActiveAiGameClient?.Dispose();
        ActiveAiGameClient = null;
    }

    private static string FindDatabasePath()
    {
        var current = AppContext.BaseDirectory;

        while (current != null)
        {
            var dbPath = Path.Combine(
                current,
                "src",
                "CaroNet.Server.Host",
                "caronet.db");

            if (File.Exists(dbPath))
            {
                return dbPath;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        // Fallback để app không crash nếu chưa tìm thấy database
        return "caronet.db";
    }

    public static IMatchHistoryStore MatchHistoryStore { get; } =
        new SqliteMatchHistoryStore(FindDatabasePath());
}