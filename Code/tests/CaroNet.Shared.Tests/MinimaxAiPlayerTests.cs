using CaroNet.Shared.Game;
using CaroNet.Shared.Game.AI;

namespace CaroNet.Shared.Tests;

public sealed class MinimaxAiPlayerTests
{
    [Fact]
    public void Ai_Should_Take_Immediate_Win()
    {
        var state = new CaroGameState(15);

        // X human, O AI. Sau chuỗi nước này O đang có 4 quân liên tiếp.
        Play(state, 5, 0, PlayerSymbol.X);
        Play(state, 4, 0, PlayerSymbol.O);
        Play(state, 5, 1, PlayerSymbol.X);
        Play(state, 4, 1, PlayerSymbol.O);
        Play(state, 6, 0, PlayerSymbol.X);
        Play(state, 4, 2, PlayerSymbol.O);
        Play(state, 6, 1, PlayerSymbol.X);
        Play(state, 4, 3, PlayerSymbol.O);
        Play(state, 7, 7, PlayerSymbol.X); // Đến lượt O (AI).

        var ai = new MinimaxAiPlayer(AiDifficulty.Easy);
        BoardPosition bestMove = ai.FindBestMove(state, PlayerSymbol.O);

        Assert.Equal(4, bestMove.Row);
        Assert.Equal(4, bestMove.Column);
    }

    [Fact]
    public void Ai_Should_Block_Immediate_Opponent_Win()
    {
        var state = new CaroGameState(15);

        // X human đang có 4 quân liên tiếp; O phải chặn tại (5,4).
        Play(state, 5, 0, PlayerSymbol.X);
        Play(state, 0, 0, PlayerSymbol.O);
        Play(state, 5, 1, PlayerSymbol.X);
        Play(state, 0, 1, PlayerSymbol.O);
        Play(state, 5, 2, PlayerSymbol.X);
        Play(state, 1, 0, PlayerSymbol.O);
        Play(state, 5, 3, PlayerSymbol.X);
        Play(state, 1, 1, PlayerSymbol.O);
        Play(state, 2, 2, PlayerSymbol.X); // Đến lượt O (AI).

        var ai = new MinimaxAiPlayer(AiDifficulty.Easy);
        BoardPosition bestMove = ai.FindBestMove(state, PlayerSymbol.O);

        Assert.Equal(5, bestMove.Row);
        Assert.Equal(4, bestMove.Column);
    }

    [Fact]
    public void Ai_Should_Return_A_Legal_Move_On_An_Active_Board()
    {
        var state = new CaroGameState(15);
        Play(state, 7, 7, PlayerSymbol.X);

        var ai = new MinimaxAiPlayer(AiDifficulty.Medium);
        BoardPosition bestMove = ai.FindBestMove(state, PlayerSymbol.O);

        Assert.InRange(bestMove.Row, 0, 14);
        Assert.InRange(bestMove.Column, 0, 14);
        Assert.Equal(CellState.Empty, state[bestMove.Row, bestMove.Column]);
    }

    private static void Play(CaroGameState state, int row, int col, PlayerSymbol player)
    {
        MoveResult result = state.MakeMove(new BoardPosition(row, col), player);
        Assert.True(result.IsSuccess, $"Nước ({row},{col}) của {player} không hợp lệ: {result.Reason}");
    }
}
