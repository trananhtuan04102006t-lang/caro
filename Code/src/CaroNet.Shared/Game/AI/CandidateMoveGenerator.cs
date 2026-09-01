using System;
using System.Collections.Generic;
using System.Linq;

namespace CaroNet.Shared.Game.AI;

internal static class CandidateMoveGenerator
{
    public static List<BoardPosition> Generate(
        SearchBoard board,
        PlayerSymbol player,
        int radius,
        int limit)
    {
        var occupied = board.Occupied;

        if (!occupied.Any())
            return [new BoardPosition(board.Size / 2, board.Size / 2)];

        var candidateSet = new HashSet<BoardPosition>();

        foreach (var stone in occupied)
        {
            for (int dr = -radius; dr <= radius; dr++)
            {
                for (int dc = -radius; dc <= radius; dc++)
                {
                    int row = stone.Row + dr;
                    int col = stone.Column + dc;

                    if (CaroRuleEngine.IsValidPosition(board.Size, row, col) &&
                        board[row, col] == CellState.Empty)
                    {
                        candidateSet.Add(new BoardPosition(row, col));
                    }
                }
            }
        }

        return candidateSet
            .Select(move => (Move: move, Score: ScoreCandidate(board, move, player)))
            .OrderByDescending(x => x.Score)
            .Take(Math.Max(1, limit))
            .Select(x => x.Move)
            .ToList();
    }

    private static int ScoreCandidate(SearchBoard board, BoardPosition move, PlayerSymbol player)
    {
        int score = 0;
        int center = board.Size / 2;
        int distance = Math.Abs(move.Row - center) + Math.Abs(move.Column - center);
        score += Math.Max(0, board.Size * 2 - distance);

        PlayerSymbol opponent = player == PlayerSymbol.X ? PlayerSymbol.O : PlayerSymbol.X;

        board.Place(move, player);
        if (board.IsWin(move, player))
            score += 1_000_000;
        board.Undo(move);

        board.Place(move, opponent);
        if (board.IsWin(move, opponent))
            score += 900_000;
        board.Undo(move);

        score += CountNearby(board, move, player) * 20;
        score += CountNearby(board, move, opponent) * 16;

        return score;
    }

    private static int CountNearby(SearchBoard board, BoardPosition move, PlayerSymbol player)
    {
        CellState target = player == PlayerSymbol.X ? CellState.X : CellState.O;
        int count = 0;

        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0)
                    continue;

                int row = move.Row + dr;
                int col = move.Column + dc;

                if (CaroRuleEngine.IsValidPosition(board.Size, row, col) && board[row, col] == target)
                    count++;
            }
        }

        return count;
    }
}
