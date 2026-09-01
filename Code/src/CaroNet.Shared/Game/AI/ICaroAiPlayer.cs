using System.Threading;

namespace CaroNet.Shared.Game.AI;

public interface ICaroAiPlayer
{
    BoardPosition FindBestMove(
        CaroGameState state,
        PlayerSymbol aiPlayer,
        CancellationToken cancellationToken = default);
}
