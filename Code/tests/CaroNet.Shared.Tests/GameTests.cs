using Xunit;
using CaroNet.Shared.Game;

namespace CaroNet.Shared.Tests;

public class CaroGameTests
{
    private CaroGameState CreateStandardGame() => new CaroGameState(15);

    [Fact]
    public void NewGame_ShouldStartWith_PlayingStatus_And_PlayerX()
    {
        var game = CreateStandardGame();

        Assert.Equal(GameStatus.Playing, game.Status);
        Assert.Equal(PlayerSymbol.X, game.CurrentPlayer);
        Assert.Equal(0, game.MoveCount);
    }

    [Fact]
    public void ValidMove_Should_UpdateGrid_And_SwitchPlayer()
    {
        var game = CreateStandardGame();

        var result = game.MakeMove(new BoardPosition(7, 7), PlayerSymbol.X);

        Assert.True(result.IsSuccess);
        Assert.Equal(CellState.X, game[7, 7]);
        Assert.Equal(PlayerSymbol.O, game.CurrentPlayer);
        Assert.Equal(1, game.MoveCount);
    }

    [Theory]
    [InlineData(-1, 0, MoveRejectReason.OutOfBounds)]
    [InlineData(15, 5, MoveRejectReason.OutOfBounds)]
    [InlineData(5, 15, MoveRejectReason.OutOfBounds)]
    public void Move_OutOfBounds_ShouldBe_Rejected(int row, int col, MoveRejectReason expectedReason)
    {
        var game = CreateStandardGame();

        var result = game.MakeMove(new BoardPosition(row, col), PlayerSymbol.X);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedReason, result.Reason);
    }

    [Fact]
    public void Move_OnOccupiedCell_ShouldBe_Rejected()
    {
        var game = CreateStandardGame();
        game.MakeMove(new BoardPosition(5, 5), PlayerSymbol.X);

        var result = game.MakeMove(new BoardPosition(5, 5), PlayerSymbol.O);

        Assert.False(result.IsSuccess);
        Assert.Equal(MoveRejectReason.CellOccupied, result.Reason);
    }

    [Fact]
    public void Move_WrongTurn_ShouldBe_Rejected()
    {
        var game = CreateStandardGame();

        var result = game.MakeMove(new BoardPosition(0, 0), PlayerSymbol.O);

        Assert.False(result.IsSuccess);
        Assert.Equal(MoveRejectReason.WrongTurn, result.Reason);
    }

    [Fact]
    public void CheckWin_Horizontal_ShouldTrigger_XWon()
    {
        var game = CreateStandardGame();

        int[,] moves = {
            {0, 0}, {1, 0},
            {0, 1}, {1, 1},
            {0, 2}, {1, 2},
            {0, 3}, {1, 3},
            {0, 4} 
        };

        MoveResult result = default;
        for (int i = 0; i < moves.GetLength(0); i++)
        {
            result = game.MakeMove(new BoardPosition(moves[i, 0], moves[i, 1]), game.CurrentPlayer);
            Assert.True(result.IsSuccess, $"Nước đi tại ({moves[i, 0]},{moves[i, 1]}) thất bại.");
        }

        Assert.Equal(GameStatus.XWon, result.Status);
        Assert.Equal(GameStatus.XWon, game.Status);
    }

    [Fact]
    public void GetWinningCells_Horizontal_ShouldReturnWinningLine()
    {
        var game = CreateStandardGame();

        int[,] moves = {
            {0, 0}, {1, 0},
            {0, 1}, {1, 1},
            {0, 2}, {1, 2},
            {0, 3}, {1, 3},
            {0, 4}
        };

        for (int i = 0; i < moves.GetLength(0); i++)
        {
            game.MakeMove(new BoardPosition(moves[i, 0], moves[i, 1]), game.CurrentPlayer);
        }

        var winningCells = CaroRuleEngine.GetWinningCells(game, 0, 4);

        Assert.Equal(5, winningCells.Count);
        Assert.Contains((0, 0), winningCells);
        Assert.Contains((0, 4), winningCells);
    }

    [Fact]
    public void CheckWin_Vertical_ShouldTrigger_OWon()
    {
        var game = CreateStandardGame();

        int[,] moves = {
            {10, 0}, {0, 2}, 
            {10, 2}, {1, 2},
            {10, 4}, {2, 2},
            {10, 6}, {3, 2}, 
            {10, 8}, {4, 2}
        };

        MoveResult result = default;
        for (int i = 0; i < moves.GetLength(0); i++)
        {
            result = game.MakeMove(new BoardPosition(moves[i, 0], moves[i, 1]), game.CurrentPlayer);
            Assert.True(result.IsSuccess, $"Nước đi tại ({moves[i, 0]},{moves[i, 1]}) thất bại.");
        }

        Assert.Equal(GameStatus.OWon, result.Status);
        Assert.Equal(GameStatus.OWon, game.Status);
    }

    [Fact]
    public void CheckWin_MainDiagonal_ShouldTrigger_XWon()
    {
        var game = CreateStandardGame();

        int[,] moves = {
            {0, 0}, {14, 0},
            {1, 1}, {14, 2},
            {2, 2}, {14, 4},
            {3, 3}, {14, 6},
            {4, 4} 
        };

        MoveResult result = default;
        for (int i = 0; i < moves.GetLength(0); i++)
        {
            result = game.MakeMove(new BoardPosition(moves[i, 0], moves[i, 1]), game.CurrentPlayer);
            Assert.True(result.IsSuccess, $"Nước đi tại ({moves[i, 0]},{moves[i, 1]}) thất bại.");
        }

        Assert.Equal(GameStatus.XWon, result.Status);
        Assert.Equal(GameStatus.XWon, game.Status);
    }

    [Fact]
    public void CheckWin_AntiDiagonal_ShouldTrigger_OWon()
    {
        var game = CreateStandardGame();

        int[,] moves = {
            {10, 0}, {0, 4}, 
            {10, 2}, {1, 3}, 
            {10, 4}, {2, 2}, 
            {10, 6}, {3, 1},
            {10, 8}, {4, 0} 
        };

        MoveResult result = default;
        for (int i = 0; i < moves.GetLength(0); i++)
        {
            result = game.MakeMove(new BoardPosition(moves[i, 0], moves[i, 1]), game.CurrentPlayer);
            Assert.True(result.IsSuccess, $"Nước đi thứ {i} tại ({moves[i, 0]}, {moves[i, 1]}) bị thất bại với lý do: {result.Reason}");
        }

        Assert.Equal(GameStatus.OWon, result.Status);
        Assert.Equal(GameStatus.OWon, game.Status);
    }

    [Fact]
    public void CheckWin_MoreThanFiveSymbols_ShouldTrigger_XWon()
    {
        var game = CreateStandardGame();

        int[,] moves = {
            {0, 0}, {5, 0}, 
            {0, 1}, {5, 2}, 
            {0, 2}, {5, 4}, 
            {0, 3}, {5, 6}, 
            {0, 5}, {5, 8}, 
            {0, 4}    
        };

        MoveResult result = default;
        for (int i = 0; i < moves.GetLength(0); i++)
        {
            result = game.MakeMove(new BoardPosition(moves[i, 0], moves[i, 1]), game.CurrentPlayer);
            Assert.True(result.IsSuccess, $"Nước đi thứ {i} tại ({moves[i, 0]}, {moves[i, 1]}) bị thất bại với lý do: {result.Reason}");
        }

        Assert.Equal(GameStatus.XWon, result.Status);
        Assert.Equal(GameStatus.XWon, game.Status);
    }

    [Fact]
    public void Move_AfterGameEnded_ShouldBe_Rejected()
    {
        var game = CreateStandardGame();
        for (int r = 0; r < 4; r++)
        {
            game.MakeMove(new BoardPosition(r, 0), PlayerSymbol.X);
            game.MakeMove(new BoardPosition(r, 1), PlayerSymbol.O);
        }
        game.MakeMove(new BoardPosition(4, 0), PlayerSymbol.X); 

        var result = game.MakeMove(new BoardPosition(5, 5), PlayerSymbol.O);

        Assert.False(result.IsSuccess);
        Assert.Equal(MoveRejectReason.GameEnded, result.Reason);
    }

    [Fact]
    public void SmallBoard_FullMoves_WithoutWin_ShouldBe_Draw()
    {
        var game = new CaroGameState(5);

        int[,] moves = {
            {0, 0}, {0, 1}, {0, 2}, {0, 4}, {0, 3},
            {1, 0}, {1, 1}, {1, 3}, {1, 2}, {1, 4},
            {2, 1}, {2, 0}, {2, 2}, {2, 3}, {2, 4},
            {3, 0}, {3, 2}, {3, 1}, {3, 4}, {3, 3},
            {4, 1}, {4, 0}, {4, 3}, {4, 2}, {4, 4}
        };

        MoveResult result = default;
        for (int i = 0; i < 25; i++)
        {
            result = game.MakeMove(new BoardPosition(moves[i, 0], moves[i, 1]), game.CurrentPlayer);
            Assert.True(result.IsSuccess, $"Nước đi thứ {i} tại ({moves[i, 0]},{moves[i, 1]}) thất bại.");
        }

        Assert.Equal(GameStatus.Draw, result.Status);
        Assert.Equal(GameStatus.Draw, game.Status);
        Assert.Equal(25, game.MoveCount);
    }
}
