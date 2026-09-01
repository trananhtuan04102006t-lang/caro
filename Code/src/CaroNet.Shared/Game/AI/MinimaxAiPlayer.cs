using System;
using System.Threading;

namespace CaroNet.Shared.Game.AI;

/// <summary>
/// AI Caro sử dụng Minimax kết hợp Alpha-Beta Pruning.
/// Candidate moves được giới hạn quanh các quân đã xuất hiện để phù hợp bàn 15x15.
/// </summary>
public sealed class MinimaxAiPlayer : ICaroAiPlayer
{
    private readonly int _maxDepth;
    private readonly int _candidateLimit;
    private readonly int _candidateRadius;

    public AiDifficulty Difficulty { get; }

    public MinimaxAiPlayer(AiDifficulty difficulty = AiDifficulty.Medium)
    {
        Difficulty = difficulty;

        ( _maxDepth, _candidateLimit, _candidateRadius ) = difficulty switch
        {
            AiDifficulty.Easy => (1, 12, 1),
            AiDifficulty.Medium => (2, 18, 2),
            AiDifficulty.Hard => (3, 24, 2),
            AiDifficulty.Expert => (4, 28, 2),
            _ => (2, 18, 2)
        };
    }

    public BoardPosition FindBestMove(
        CaroGameState state,
        PlayerSymbol aiPlayer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Status != GameStatus.Playing)
            return FindFirstEmpty(state);

        if (state.CurrentPlayer != aiPlayer)
            throw new InvalidOperationException("Không thể tìm nước đi khi chưa tới lượt AI.");

        var board = SearchBoard.FromGameState(state);
        PlayerSymbol opponent = Opponent(aiPlayer);

        // 1. Nếu AI có nước thắng ngay, luôn đánh nước đó.
        foreach (var move in CandidateMoveGenerator.Generate(board, aiPlayer, _candidateRadius, _candidateLimit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            board.Place(move, aiPlayer);
            bool wins = board.IsWin(move, aiPlayer);
            board.Undo(move);

            if (wins)
                return move;
        }

        // 2. Nếu đối thủ có nước thắng ngay, ưu tiên chặn.
        foreach (var move in CandidateMoveGenerator.Generate(board, opponent, _candidateRadius, _candidateLimit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            board.Place(move, opponent);
            bool wins = board.IsWin(move, opponent);
            board.Undo(move);

            if (wins)
                return move;
        }

        var candidates = CandidateMoveGenerator.Generate(
            board,
            aiPlayer,
            _candidateRadius,
            _candidateLimit);

        if (candidates.Count == 0)
            return FindFirstEmpty(state);

        int bestScore = int.MinValue;
        BoardPosition bestMove = candidates[0];
        int alpha = int.MinValue + 1;
        int beta = int.MaxValue;

        foreach (var move in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            board.Place(move, aiPlayer);
            int score = Minimax(
                board,
                _maxDepth - 1,
                maximizing: false,
                aiPlayer,
                opponent,
                alpha,
                beta,
                cancellationToken);
            board.Undo(move);

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }

            alpha = Math.Max(alpha, bestScore);
        }

        return bestMove;
    }

    private int Minimax(
        SearchBoard board,
        int depth,
        bool maximizing,
        PlayerSymbol aiPlayer,
        PlayerSymbol opponent,
        int alpha,
        int beta,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (depth <= 0 || board.IsFull)
            return BoardEvaluator.Evaluate(board, aiPlayer);

        var player = maximizing ? aiPlayer : opponent;
        var candidates = CandidateMoveGenerator.Generate(
            board,
            player,
            _candidateRadius,
            _candidateLimit);

        if (candidates.Count == 0)
            return BoardEvaluator.Evaluate(board, aiPlayer);

        if (maximizing)
        {
            int value = int.MinValue + 1;

            foreach (var move in candidates)
            {
                board.Place(move, player);

                int childValue = board.IsWin(move, player)
                    ? BoardEvaluator.WinScore
                    : Minimax(
                        board,
                        depth - 1,
                        maximizing: false,
                        aiPlayer,
                        opponent,
                        alpha,
                        beta,
                        cancellationToken);

                board.Undo(move);
                value = Math.Max(value, childValue);
                alpha = Math.Max(alpha, value);

                if (alpha >= beta)
                    break;
            }

            return value;
        }
        else
        {
            int value = int.MaxValue;

            foreach (var move in candidates)
            {
                board.Place(move, player);

                int childValue = board.IsWin(move, player)
                    ? -BoardEvaluator.WinScore
                    : Minimax(
                        board,
                        depth - 1,
                        maximizing: true,
                        aiPlayer,
                        opponent,
                        alpha,
                        beta,
                        cancellationToken);

                board.Undo(move);
                value = Math.Min(value, childValue);
                beta = Math.Min(beta, value);

                if (alpha >= beta)
                    break;
            }

            return value;
        }
    }

    private static PlayerSymbol Opponent(PlayerSymbol player) =>
        player == PlayerSymbol.X ? PlayerSymbol.O : PlayerSymbol.X;

    private static BoardPosition FindFirstEmpty(CaroGameState state)
    {
        for (int row = 0; row < state.Size; row++)
        {
            for (int col = 0; col < state.Size; col++)
            {
                if (state[row, col] == CellState.Empty)
                    return new BoardPosition(row, col);
            }
        }

        return new BoardPosition(0, 0);
    }
}
