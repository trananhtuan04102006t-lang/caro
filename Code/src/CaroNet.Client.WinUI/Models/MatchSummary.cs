using System;

namespace CaroNet.Client.WinUI.Models;

public sealed class MatchSummary
{
    public string PlayerX { get; init; } = "";

    public string PlayerO { get; init; } = "";

    public string Winner { get; init; } = "";

    public DateTime PlayedAt { get; init; }

    public int MoveCount { get; init; }
}