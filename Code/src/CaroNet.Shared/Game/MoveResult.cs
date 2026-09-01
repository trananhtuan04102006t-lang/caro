using System;
using System.Collections.Generic;
using System.Text;

namespace CaroNet.Shared.Game;

public enum MoveRejectReason
{
    None,
    GameEnded,
    OutOfBounds,
    CellOccupied,
    WrongTurn
}

public readonly record struct MoveResult(bool IsSuccess, GameStatus Status, MoveRejectReason Reason = MoveRejectReason.None);