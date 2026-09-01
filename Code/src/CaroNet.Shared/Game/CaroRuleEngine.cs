using System.Collections.Generic;

namespace CaroNet.Shared.Game;

public static class CaroRuleEngine
{
    private static readonly (int dRow, int dCol)[] Directions =
    {
        (0, 1),
        (1, 0),
        (1, 1),
        (1, -1)
    };

    public static bool IsValidPosition(int size, int r, int c)
    {
        return r >= 0 && r < size && c >= 0 && c < size;
    }
    public static List<(int Row, int Col)> GetWinningCells(CaroGameState state, int lastRow, int lastCol)
    {
        var winningCells = new List<(int Row, int Col)>();
        CellState targetState = state[lastRow, lastCol];
        if (targetState == CellState.Empty) return winningCells;

        int size = state.Size;

        foreach (var (dRow, dCol) in Directions)
        {
            var currentLine = new List<(int Row, int Col)> { (lastRow, lastCol) };

            int rPos = lastRow + dRow;
            int cPos = lastCol + dCol;
            while (IsValidPosition(size, rPos, cPos) && state[rPos, cPos] == targetState)
            {
                currentLine.Add((rPos, cPos));
                rPos += dRow;
                cPos += dCol;
            }

            int rNeg = lastRow - dRow;
            int cNeg = lastCol - dCol;
            while (IsValidPosition(size, rNeg, cNeg) && state[rNeg, cNeg] == targetState)
            {
                currentLine.Add((rNeg, cNeg));
                rNeg -= dRow;
                cNeg -= dCol;
            }

            if (currentLine.Count >= 5)
            {
                return currentLine;
            }
        }
        return winningCells;
    }

    public static bool CheckWin(CaroGameState state, int lastRow, int lastCol)
    {
        return GetWinningCells(state, lastRow, lastCol).Count >= 5;
    }
}