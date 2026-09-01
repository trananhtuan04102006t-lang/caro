using System;

namespace CaroNet.Shared.Game;

public class CaroGameState
{
    public int Size { get; }
    public PlayerSymbol CurrentPlayer { get; private set; }
    public GameStatus Status { get; private set; }
    public int MoveCount { get; private set; }

    private readonly CellState[,] _grid;

    public CellState this[int r, int c]
    {
        get
        {
            if (!CaroRuleEngine.IsValidPosition(Size, r, c))
                throw new ArgumentOutOfRangeException(nameof(r), "Tọa độ nằm ngoài bàn cờ.");
            return _grid[r, c];
        }
    }

    public CaroGameState(int size = 15)
    {
        if (size < 5 || size > 20)
            throw new ArgumentOutOfRangeException(nameof(size), "Kích thước bàn cờ phải từ 5 đến 20.");

        Size = size;
        _grid = new CellState[size, size];
        Reset();
    }

    public MoveResult MakeMove(BoardPosition position, PlayerSymbol player)
    {
        int r = position.Row;
        int c = position.Column;

        if (Status != GameStatus.Playing)
            return new MoveResult(false, Status, MoveRejectReason.GameEnded);

        if (!CaroRuleEngine.IsValidPosition(Size, r, c))
            return new MoveResult(false, Status, MoveRejectReason.OutOfBounds);

        if (_grid[r, c] != CellState.Empty)
            return new MoveResult(false, Status, MoveRejectReason.CellOccupied);

        if (player != CurrentPlayer)
            return new MoveResult(false, Status, MoveRejectReason.WrongTurn);

        _grid[r, c] = (player == PlayerSymbol.X) ? CellState.X : CellState.O;
        MoveCount++;

        if (CaroRuleEngine.CheckWin(this, r, c))
        {
            Status = (player == PlayerSymbol.X) ? GameStatus.XWon : GameStatus.OWon;
        }
        else if (MoveCount == Size * Size)
        {
            Status = GameStatus.Draw;
        }
        else
        {
            CurrentPlayer = (CurrentPlayer == PlayerSymbol.X) ? PlayerSymbol.O : PlayerSymbol.X;
        }

        return new MoveResult(true, Status);
    }

    public void Reset()
    {
        Array.Clear(_grid, 0, _grid.Length);
        MoveCount = 0;
        CurrentPlayer = PlayerSymbol.X;
        Status = GameStatus.Playing;
    }

    public void ResetForRematch(PlayerSymbol startingPlayer)
    {
        Array.Clear(_grid, 0, _grid.Length);
        MoveCount = 0;
        CurrentPlayer = startingPlayer;
        Status = GameStatus.Playing;
    }

    public void EndByResignation(PlayerSymbol winner)
    {
        if (Status != GameStatus.Playing)
            return;

        Status = winner == PlayerSymbol.X ? GameStatus.XWon : GameStatus.OWon;
    }

    public void EndAsDraw()
    {
        if (Status != GameStatus.Playing)
            return;

        Status = GameStatus.Draw;
    }

    public void EndByTimeout(PlayerSymbol winner)
    {
        if (Status != GameStatus.Playing)
            return;

        Status = winner == PlayerSymbol.X ? GameStatus.XWon : GameStatus.OWon;
    }
}
