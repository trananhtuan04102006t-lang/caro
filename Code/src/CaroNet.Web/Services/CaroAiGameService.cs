using System.Collections.Concurrent;
using CaroNet.Shared.Game;
using CaroNet.Shared.Game.AI;

namespace CaroNet.Web.Services;

public sealed class CaroAiGameService
{
    private readonly ConcurrentDictionary<Guid, GameSession> _games = new();

    public GameSession CreateGame(AiDifficulty difficulty)
    {
        var gameId = Guid.NewGuid();

        var state = new CaroGameState(15);
        var ai = new MinimaxAiPlayer(difficulty);

        var game = new GameSession(
            gameId,
            state,
            ai);

        _games[gameId] = game;

        return game;
    }

    public GameSession? GetGame(Guid gameId)
    {
        _games.TryGetValue(gameId, out var game);
        return game;
    }

    public bool RemoveGame(Guid gameId)
    {
        return _games.TryRemove(gameId, out _);
    }
}


public sealed class GameSession
{
    public Guid Id { get; }

    public CaroGameState State { get; }

    public MinimaxAiPlayer Ai { get; }

    public PlayerSymbol HumanPlayer { get; } = PlayerSymbol.X;

    public PlayerSymbol AiPlayer { get; } = PlayerSymbol.O;

    public object SyncRoot { get; } = new();

    public GameSession(
        Guid id,
        CaroGameState state,
        MinimaxAiPlayer ai)
    {
        Id = id;
        State = state;
        Ai = ai;
    }
}