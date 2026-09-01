using System;

namespace CaroNet.Shared.Game.AI;

internal static class BoardEvaluator
{
    public const int WinScore = 10_000_000;

    public static int Evaluate(SearchBoard board, PlayerSymbol aiPlayer)
    {
        PlayerSymbol opponent = aiPlayer == PlayerSymbol.X ? PlayerSymbol.O : PlayerSymbol.X;

        int aiScore = EvaluatePlayer(board, aiPlayer);
        int opponentScore = EvaluatePlayer(board, opponent);

        return aiScore - opponentScore;
    }

    private static int EvaluatePlayer(SearchBoard board, PlayerSymbol player)
    {
        CellState target = player == PlayerSymbol.X ? CellState.X : CellState.O;
        int score = 0;

        for (int row = 0; row < board.Size; row++)
        {
            for (int col = 0; col < board.Size; col++)
            {
                if (board[row, col] != target)
                    continue;

                // Chỉ bắt đầu đếm chuỗi tại đầu chuỗi để tránh tính lặp.
                if (col > 0 && board[row, col - 1] == target)
                    continue;

                score += ScoreLine(board, row, col, 0, 1, target);
            }
        }

        for (int row = 0; row < board.Size; row++)
        {
            for (int col = 0; col < board.Size; col++)
            {
                if (board[row, col] != target)
                    continue;

                if (row > 0 && board[row - 1, col] == target)
                    continue;

                score += ScoreLine(board, row, col, 1, 0, target);
            }
        }

        for (int row = 0; row < board.Size; row++)
        {
            for (int col = 0; col < board.Size; col++)
            {
                if (board[row, col] != target)
                    continue;

                if (row > 0 && col > 0 && board[row - 1, col - 1] == target)
                    continue;

                score += ScoreLine(board, row, col, 1, 1, target);
            }
        }

        for (int row = 0; row < board.Size; row++)
        {
            for (int col = 0; col < board.Size; col++)
            {
                if (board[row, col] != target)
                    continue;

                if (row > 0 && col + 1 < board.Size && board[row - 1, col + 1] == target)
                    continue;

                score += ScoreLine(board, row, col, 1, -1, target);
            }
        }

        return score;
    }

    private static int ScoreLine(
        SearchBoard board,
        int row,
        int col,
        int dRow,
        int dCol,
        CellState target)
    {
        int length = 0;
        int r = row;
        int c = col;

        while (CaroRuleEngine.IsValidPosition(board.Size, r, c) && board[r, c] == target)
        {
            length++;
            r += dRow;
            c += dCol;
        }

        if (length >= 5)
            return WinScore;

        int openEnds = 0;

        int beforeRow = row - dRow;
        int beforeCol = col - dCol;
        if (CaroRuleEngine.IsValidPosition(board.Size, beforeRow, beforeCol) &&
            board[beforeRow, beforeCol] == CellState.Empty)
        {
            openEnds++;
        }

        if (CaroRuleEngine.IsValidPosition(board.Size, r, c) &&
            board[r, c] == CellState.Empty)
        {
            openEnds++;
        }

        return length switch
        {
            4 when openEnds == 2 => 100_000,
            4 when openEnds == 1 => 10_000,
            3 when openEnds == 2 => 5_000,
            3 when openEnds == 1 => 500,
            2 when openEnds == 2 => 100,
            2 when openEnds == 1 => 20,
            1 when openEnds == 2 => 5,
            _ => 1
        };
    }
}
