using System;
using System.Collections.Generic;

namespace CaroNet.Shared.Game.AI;

internal sealed class SearchBoard
{
    private static readonly (int dRow, int dCol)[] Directions =
    {
        (0, 1),
        (1, 0),
        (1, 1),
        (1, -1)
    };

    private readonly CellState[,] _cells;
    private readonly HashSet<BoardPosition> _occupied = [];

    public int Size { get; }
    public int MoveCount { get; private set; }
    public bool IsFull => MoveCount >= Size * Size;
    public IEnumerable<BoardPosition> Occupied => _occupied;

    private SearchBoard(int size)
    {
        Size = size;
        _cells = new CellState[size, size];
    }

    public CellState this[int row, int column] => _cells[row, column];

    public static SearchBoard FromGameState(CaroGameState state)
    {
        var board = new SearchBoard(state.Size);

        for (int row = 0; row < state.Size; row++)
        {
            for (int col = 0; col < state.Size; col++)
            {
                CellState cell = state[row, col];
                board._cells[row, col] = cell;

                if (cell != CellState.Empty)
                {
                    board.MoveCount++;
                    board._occupied.Add(new BoardPosition(row, col));
                }
            }
        }

        return board;
    }

    public void Place(BoardPosition position, PlayerSymbol player)
    {
        if (!CaroRuleEngine.IsValidPosition(Size, position.Row, position.Column))
            throw new ArgumentOutOfRangeException(nameof(position));

        if (_cells[position.Row, position.Column] != CellState.Empty)
            throw new InvalidOperationException("Ô đang được đánh trong trạng thái tìm kiếm.");

        _cells[position.Row, position.Column] = ToCell(player);
        _occupied.Add(position);
        MoveCount++;
    }

    public void Undo(BoardPosition position)
    {
        if (_cells[position.Row, position.Column] == CellState.Empty)
            return;

        _cells[position.Row, position.Column] = CellState.Empty;
        _occupied.Remove(position);
        MoveCount--;
    }

    public bool IsWin(BoardPosition lastMove, PlayerSymbol player)
    {
        CellState target = ToCell(player);

        foreach (var (dRow, dCol) in Directions)
        {
            int count = 1;
            count += Count(lastMove.Row, lastMove.Column, dRow, dCol, target);
            count += Count(lastMove.Row, lastMove.Column, -dRow, -dCol, target);

            if (count >= 5)
                return true;
        }

        return false;
    }

    private int Count(int row, int col, int dRow, int dCol, CellState target)
    {
        int count = 0;
        row += dRow;
        col += dCol;

        while (CaroRuleEngine.IsValidPosition(Size, row, col) && _cells[row, col] == target)
        {
            count++;
            row += dRow;
            col += dCol;
        }

        return count;
    }

    private static CellState ToCell(PlayerSymbol player) =>
        player == PlayerSymbol.X ? CellState.X : CellState.O;
}
